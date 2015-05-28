/*
 * Copyright 2006 The Android Open Source Project
 * Copyright 2015 Yasuyuki Hamada <yasuyuki_hamada@agri-info-design.com>
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *      http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 * 
 * Project home page: https://github.com/ysykhmd/usb-serial-for-xamarin-android
 * 
 * This project is based on usb-serial-for-android and ported for Xamarin.Android.
 * Original project home page: https://github.com/mik3y/usb-serial-for-android
 */

using System;
using System.Text;

namespace UsbSerialExamples
{
    /**
     * Clone of Android's HexDump class, for use in debugging. Cosmetic changes
     * only.
     */
    public class HexDump
    {
        private static char[] HEX_DIGITS = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        public static string DumpHexString(byte[] array)
        {
            return DumpHexString(array, 0, array.Length);
        }

        public static string DumpHexString(byte[] array, int offset, int length)
        {
            StringBuilder result = new StringBuilder();

            byte[] line = new byte[16];
            int lineIndex = 0;

            result.Append("\n0x");
            result.Append(ToHexString(offset));

            for (int i = offset; i < offset + length; i++)
            {
                if (lineIndex == 16)
                {
                    result.Append(" ");

                    for (int j = 0; j < 16; j++)
                    {
                        if (line[j] > ' ' && line[j] < '~')
                        {
                            //result.Append(new String(line, j, 1));
                        }
                        else
                        {
                            result.Append(".");
                        }
                    }

                    result.Append("\n0x");
                    result.Append(ToHexString(i));
                    lineIndex = 0;
                }

                byte b = array[i];
                result.Append(" ");
                result.Append(HEX_DIGITS[(b >> 4) & 0x0F]);
                result.Append(HEX_DIGITS[b & 0x0F]);

                line[lineIndex++] = b;
            }

            if (lineIndex != 16)
            {
                int count = (16 - lineIndex) * 3;
                count++;
                for (int i = 0; i < count; i++)
                {
                    result.Append(" ");
                }

                for (int i = 0; i < lineIndex; i++)
                {
                    if (line[i] > ' ' && line[i] < '~')
                    {
                        //result.Append(new String(line, i, 1));
                    }
                    else
                    {
                        result.Append(".");
                    }
                }
            }

            return result.ToString();
        }

        public static string ToHexString(byte b)
        {
            return ToHexString(ToByteArray(b));
        }

        public static string ToHexString(byte[] array)
        {
            return ToHexString(array, 0, array.Length);
        }

        public static string ToHexString(byte[] array, int offset, int length)
        {
            char[] buf = new char[length * 2];

            int bufIndex = 0;
            for (int i = offset; i < offset + length; i++)
            {
                byte b = array[i];
                buf[bufIndex++] = HEX_DIGITS[(b >> 4) & 0x0F];
                buf[bufIndex++] = HEX_DIGITS[b & 0x0F];
            }

            return new String(buf);
        }

        public static string ToHexString(int i)
        {
            return ToHexString(ToByteArray(i));
        }

        public static string toHexString(short i)
        {
            return ToHexString(ToByteArray(i));
        }

        public static byte[] ToByteArray(byte b)
        {
            byte[] array = new byte[1];
            array[0] = b;
            return array;
        }

        public static byte[] ToByteArray(int i)
        {
            byte[] array = new byte[4];

            array[3] = (byte)(i & 0xFF);
            array[2] = (byte)((i >> 8) & 0xFF);
            array[1] = (byte)((i >> 16) & 0xFF);
            array[0] = (byte)((i >> 24) & 0xFF);

            return array;
        }

        public static byte[] ToByteArray(short i)
        {
            byte[] array = new byte[2];

            array[1] = (byte)(i & 0xFF);
            array[0] = (byte)((i >> 8) & 0xFF);

            return array;
        }

        private static int ToByte(char c)
        {
            if (c >= '0' && c <= '9')
                return (c - '0');
            if (c >= 'A' && c <= 'F')
                return (c - 'A' + 10);
            if (c >= 'a' && c <= 'f')
                return (c - 'a' + 10);

            throw new ArgumentOutOfRangeException("Invalid hex char '" + c + "'");
        }

        public static byte[] HexStringToByteArray(string hexString)
        {
            int length = hexString.Length;
            byte[] buffer = new byte[length / 2];
            char[] charBuffer = hexString.ToCharArray();

            for (int i = 0; i < length; i += 2)
            {
                buffer[i / 2] = (byte)((ToByte(charBuffer[i]) << 4) | ToByte(charBuffer[i + 1]));
            }

            return buffer;
        }
    }
}