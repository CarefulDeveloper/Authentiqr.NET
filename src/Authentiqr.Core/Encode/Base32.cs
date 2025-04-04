﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Authentiqr.Core.Encode
{
    public class Base32
    {
        // Original file source: http://www.codeproject.com/Tips/76650/Base32-base32url-base64url-and-z-base-32-encoding-.aspx
        // Modifications by Richard Green

        public const char StandardPaddingChar = '=';
        public const string Base32StandardAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        public const string ZBase32Alphabet = "ybndrfg8ejkmcpqxot1uwisza345h769";

        public char PaddingChar;
        public bool UsePadding;
        public bool IsCaseSensitive;
        public bool IgnoreWhiteSpaceWhenDecoding;

        private readonly string _alphabet;
        private Dictionary<string, uint> _index;

        // alphabets may be used with varying case sensitivity, thus index must not ignore case
        private static readonly Dictionary<string, Dictionary<string, uint>> _indexes = new(2, StringComparer.InvariantCulture);

        /// <summary>
        /// Create case insensitive encoder/decoder using the standard base32 alphabet without padding.
        /// White space is not permitted when decoding (not ignored).
        /// </summary>
        public Base32() : this(false, false, false, Base32StandardAlphabet) { }

        /// <summary>
        /// Create case insensitive encoder/decoder using the standard base32 alphabet.
        /// White space is not permitted when decoding (not ignored).
        /// </summary>
        /// <param name="padding">Require/use padding characters?</param>
        public Base32(bool padding) : this(padding, false, false, Base32StandardAlphabet) { }

        /// <summary>
        /// Create encoder/decoder using the standard base32 alphabet.
        /// White space is not permitted when decoding (not ignored).
        /// </summary>
        /// <param name="padding">Require/use padding characters?</param>
        /// <param name="caseSensitive">Be case sensitive when decoding?</param>
        public Base32(bool padding, bool caseSensitive) : this(padding, caseSensitive, false, Base32StandardAlphabet) { }

        /// <summary>
        /// Create encoder/decoder using the standard base32 alphabet.
        /// </summary>
        /// <param name="padding">Require/use padding characters?</param>
        /// <param name="caseSensitive">Be case sensitive when decoding?</param>
        /// <param name="ignoreWhiteSpaceWhenDecoding">Ignore / allow white space when decoding?</param>
        public Base32(bool padding, bool caseSensitive, bool ignoreWhiteSpaceWhenDecoding) : this(padding, caseSensitive, ignoreWhiteSpaceWhenDecoding, Base32StandardAlphabet) { }

        /// <summary>
        /// Create case insensitive encoder/decoder with alternative alphabet and no padding.
        /// White space is not permitted when decoding (not ignored).
        /// </summary>
        /// <param name="alternateAlphabet">Alphabet to use (such as Base32Url.ZBase32Alphabet)</param>
        public Base32(string alternateAlphabet) : this(false, false, false, alternateAlphabet) { }

        /// <summary>
        /// Create the encoder/decoder specifying all options manually.
        /// </summary>
        /// <param name="padding">Require/use padding characters?</param>
        /// <param name="caseSensitive">Be case sensitive when decoding?</param>
        /// <param name="ignoreWhiteSpaceWhenDecoding">Ignore / allow white space when decoding?</param>
        /// <param name="alternateAlphabet">Alphabet to use (such as Base32Url.ZBase32Alphabet, Base32Url.Base32StandardAlphabet or your own custom 32 character alphabet string)</param>
        public Base32(bool padding, bool caseSensitive, bool ignoreWhiteSpaceWhenDecoding, string alternateAlphabet)
        {
            if (alternateAlphabet.Length != 32)
            {
                throw new ArgumentException("Alphabet must be exactly 32 characters long for base 32 encoding.");
            }

            PaddingChar = StandardPaddingChar;
            UsePadding = padding;
            IsCaseSensitive = caseSensitive;
            IgnoreWhiteSpaceWhenDecoding = ignoreWhiteSpaceWhenDecoding;

            _alphabet = alternateAlphabet;
        }

        /// <summary>
        /// Decode a base32 string to a byte[] using the default options
        /// (case insensitive without padding using the standard base32 alphabet from rfc4648).
        /// White space is not permitted (not ignored).
        /// Use alternative constructors for more options.
        /// </summary>
        public static byte[] FromBase32String(string input)
        {
            return new Base32(false, false, true).Decode(input);
        }

        /// <summary>
        /// Encode a base32 string from a byte[] using the default options
        /// (case insensitive without padding using the standard base32 alphabet from rfc4648).
        /// Use alternative constructors for more options.
        /// </summary>
        public static string ToBase32String(byte[] data)
        {
            return new Base32().Encode(data);
        }

        public string Encode(byte[] data)
        {
            var result = new StringBuilder(Math.Max((int)Math.Ceiling(data.Length * 8 / 5.0), 1));

            var emptyBuff = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0 };
            var buff = new byte[8];

            // take input five bytes at a time to chunk it up for encoding
            for (var i = 0; i < data.Length; i += 5)
            {
                var bytes = Math.Min(data.Length - i, 5);

                // parse five bytes at a time using an 8 byte ulong
                Array.Copy(emptyBuff, buff, emptyBuff.Length);
                Array.Copy(data, i, buff, buff.Length - (bytes + 1), bytes);
                Array.Reverse(buff);
                var val = BitConverter.ToUInt64(buff, 0);

                for (int bitOffset = ((bytes + 1) * 8) - 5; bitOffset > 3; bitOffset -= 5)
                {
                    result.Append(_alphabet[(int)((val >> bitOffset) & 0x1f)]);
                }
            }

            if (UsePadding)
            {
                result.Append(string.Empty.PadRight((result.Length % 8) == 0 ? 0 : (8 - (result.Length % 8)), PaddingChar));
            }

            return result.ToString();
        }

        public byte[] Decode(string input)
        {
            if (IgnoreWhiteSpaceWhenDecoding)
            {
                input = Regex.Replace(input, "\\s+", "");
            }

            if (UsePadding)
            {
                if (input.Length % 8 != 0)
                {
                    throw new ArgumentException("Invalid length for a base32 string with padding.");
                }

                input = input.TrimEnd(PaddingChar);
            }

            // index the alphabet for decoding only when needed
            EnsureAlphabetIndexed();

            var ms = new MemoryStream(Math.Max((int)Math.Ceiling(input.Length * 5 / 8.0), 1));

            // take input eight bytes at a time to chunk it up for encoding
            for (int i = 0; i < input.Length; i += 8)
            {
                var chars = Math.Min(input.Length - i, 8);
                var bytes = (int)Math.Floor(chars * (5 / 8.0));

                ulong val = 0;

                for (int charOffset = 0; charOffset < chars; charOffset++)
                {
                    if (!_index.TryGetValue(input.Substring(i + charOffset, 1), out uint cbyte))
                    {
                        throw new ArgumentException(string.Concat("Invalid character '", input[i+charOffset], "' in base32 string, valid characters are: ", _alphabet));
                    }

                    val |= ((ulong)cbyte) << (((bytes + 1) * 8) - (charOffset * 5) - 5);
                }

                var buff = BitConverter.GetBytes(val);
                Array.Reverse(buff);
                ms.Write(buff, buff.Length - (bytes + 1), bytes);
            }

            return ms.ToArray();
        }

        private void EnsureAlphabetIndexed()
        {
            if (_index == null)
            {
                string indexKey = (IsCaseSensitive ? "S" : "I") + _alphabet;

                if (!_indexes.TryGetValue(indexKey, out Dictionary<string, uint> cidx))
                {
                    lock (_indexes)
                    {
                        if (!_indexes.TryGetValue(indexKey, out cidx))
                        {
                            cidx = new Dictionary<string, uint>(_alphabet.Length, IsCaseSensitive ? StringComparer.InvariantCulture : StringComparer.InvariantCultureIgnoreCase);
                            for (int i = 0; i < _alphabet.Length; i++)
                            {
                                cidx[_alphabet.Substring(i, 1)] = (uint)i;
                            }
                            _indexes.Add(indexKey, cidx);
                        }
                    }
                }

                _index = cidx;
            }
        }
    }
}
