﻿/*
 * Copyright (C) 2016 Benjamin Krämer
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 */

using System;
using System.Collections.Generic;
using System.IO;
using LibAmiibo.Data.Figurine;
using LibAmiibo.Data.Settings;
using LibAmiibo.Helper;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace LibAmiibo.Data
{
    public class AmiiboTag
    {
        /// <summary>
        /// 
        /// This can be an encrypted tag converted with NtagHelpers.GetInternalTag() or an decrypted tag
        /// unpacked with Nfc3DAmiiboKeys.Unpack()
        /// </summary>
        public ArraySegment<byte> InternalTag { get; }

        private Amiibo amiibo;
        public Amiibo Amiibo
        {
            get { return amiibo; }
            set
            {
                if (value == null)
                    throw new ArgumentNullException(nameof(value));

                this.amiibo = value;
                var amiiboBuffer = new ArraySegment<byte>(InternalTag.Array, 0x1DC, 0x08);
                amiiboBuffer.CopyFrom(NtagHelpers.StringToByteArrayFastest(value.StatueId));
            }
        }

        public AmiiboSettings AmiiboSettings { get; }

        /// <summary>
        /// Only valid if HasAppData returns true. Use AmiiboSettings.AmiiboAppData for easier access.
        /// </summary>
        public ArraySegment<byte> AppData
        {
            get
            {
                return new ArraySegment<byte>(InternalTag.Array, 0x0DC, 0xD8);
            }
            set { AppData.CopyFrom(value); }
        }
        public bool IsDecrypted { get; private set; }

        public bool HasAppData
        {
            get { return IsDecrypted && AmiiboSettings.Status.HasFlag(Status.AppDataInitialized); }
        }

        public bool HasUserData
        {
            get { return IsDecrypted && AmiiboSettings.Status.HasFlag(Status.UserDataInitialized); }
        }

        public ArraySegment<byte> DataSignature
        {
            get
            {
                return new ArraySegment<byte>(InternalTag.Array, 0x008, 0x20);
            }
            set { DataSignature.CopyFrom(value); }
        }
        /// <summary>
        /// Signs InternalTag from 0x1D4 to 0x208.
        /// </summary>
        public ArraySegment<byte> TagSignature
        {
            get
            {
                return new ArraySegment<byte>(InternalTag.Array, 0x1B4, 0x20);
            }
            set { TagSignature.CopyFrom(value); }
        }

        public ArraySegment<byte> CryptoInitSequence
        {
            get
            {
                return new ArraySegment<byte>(InternalTag.Array, 0x028, 0x004);
            }
            set { CryptoInitSequence.CopyFrom(value); }
        }

        public ushort WriteCounter
        {
            get { return NtagHelpers.UInt16FromTag(InternalTag, 0x029); }
            set { NtagHelpers.UInt16ToTag(InternalTag, 0x029, value); }
        }

        public ArraySegment<byte> CryptoBuffer
        {
            get
            {
                return new ArraySegment<byte>(InternalTag.Array, 0x02C, 0x188);
            }
            set { CryptoBuffer.CopyFrom(value); }
        }

        public ArraySegment<byte> NtagSerial
        {
            get
            {
                return new ArraySegment<byte>(InternalTag.Array, 0x1D4, 0x008);
            }
            set { NtagSerial.CopyFrom(value); }
        }

        public string UID
        {
            get
            {
                IList<byte> ntagSerial = this.NtagSerial;
                return String.Format("{0:X2} {1:X2} {2:X2} {3:X2} {4:X2} {5:X2} {6:X2}", ntagSerial[0], ntagSerial[1], ntagSerial[2], ntagSerial[4], ntagSerial[5], ntagSerial[6], ntagSerial[7]);
            }
        }

        public ArraySegment<byte> PlaintextData
        {
            get
            {
                return new ArraySegment<byte>(InternalTag.Array, 0x1DC, 0x02C);
            }
            set { PlaintextData.CopyFrom(value); }
        }

        public ArraySegment<byte> NtagECDSASignature
        {
            get
            {
                return new ArraySegment<byte>(InternalTag.Array, 0x208, 0x020);
            }
            set { NtagECDSASignature.CopyFrom(value); }
        }

        private AmiiboTag(ArraySegment<byte> internalTag)
        {
            this.InternalTag = internalTag;
            this.Amiibo = Amiibo.FromInternalTag(internalTag);
            this.AmiiboSettings = new AmiiboSettings(CryptoBuffer, AppData);
        }

        public static AmiiboTag FromNtagData(byte[] data)
        {
            return new AmiiboTag(new ArraySegment<byte>(NtagHelpers.GetInternalTag(data))) { IsDecrypted = false };
        }

        public static AmiiboTag FromInternalTag(ArraySegment<byte> data)
        {
            return new AmiiboTag(data) { IsDecrypted = true };
        }

        public static AmiiboTag DecryptWithKeys(byte[] data)
        {
            byte[] decryptedData = new byte[NtagHelpers.NFC3D_AMIIBO_SIZE];
            var amiiboKeys = Files.AmiiboKeys;

            return amiiboKeys != null && amiiboKeys.Unpack(data, decryptedData)
                ? FromInternalTag(new ArraySegment<byte>(decryptedData))
                : FromNtagData(data);
        }

        public byte[] EncryptWithKeys()
        {
            byte[] encryptedData = new byte[NtagHelpers.NFC3D_NTAG_SIZE];
            var amiiboKeys = Files.AmiiboKeys;

            if (amiiboKeys == null)
                return null;

            amiiboKeys.Pack(this.InternalTag.Array, encryptedData);
            return encryptedData;
        }

        public bool IsNtagECDSASignatureValid()
        {
            if (NtagECDSASignature.Count != 0x20 || NtagSerial.Count != 8)
                return false;

            var r = new byte[NtagECDSASignature.Count / 2];
            var s = new byte[NtagECDSASignature.Count / 2];
            Array.Copy(NtagECDSASignature.Array, NtagECDSASignature.Offset, r, 0, r.Length);
            Array.Copy(NtagECDSASignature.Array, NtagECDSASignature.Offset + r.Length, s, 0, s.Length);

            var uid = new byte[7];
            Array.Copy(NtagSerial.Array, NtagSerial.Offset + 0, uid, 0, 3);
            Array.Copy(NtagSerial.Array, NtagSerial.Offset + 4, uid, 3, 4);

            var curve = SecNamedCurves.GetByName("secp128r1");
            var curveSpec = new ECDomainParameters(curve.Curve, curve.G, curve.N, curve.H, curve.GetSeed());
            var key = new ECPublicKeyParameters("ECDSA", curve.Curve.DecodePoint(NtagHelpers.NTAG_PUB_KEY), curveSpec);

            var signer = SignerUtilities.GetSigner("NONEwithECDSA");

            signer.Init(false, key);

            signer.BlockUpdate(uid, 0, uid.Length);

            using (var ms = new MemoryStream())
            using (var der = new Asn1OutputStream(ms))
            {
                var v = new Asn1EncodableVector
                {
                    new DerInteger(new BigInteger(1, r)),
                    new DerInteger(new BigInteger(1, s))
                };
                der.WriteObject(new DerSequence(v));

                return signer.VerifySignature(ms.ToArray());
            }
        }
    }
}
