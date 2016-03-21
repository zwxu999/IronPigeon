﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the Microsoft Reciprocal License (Ms-RL) license. See LICENSE file in the project root for full license information.

namespace IronPigeon.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Runtime.Serialization;
    using System.Text;
    using System.Threading.Tasks;
    using NUnit.Framework;

    [TestFixture]
    public class AddressBookEntryTest
    {
        private CryptoSettings desktopCryptoProvider;

        [SetUp]
        public void Setup()
        {
            this.desktopCryptoProvider = TestUtilities.CreateAuthenticCryptoProvider();
        }

        [Test]
        public void Ctor()
        {
            var entry = new AddressBookEntry();
            Assert.That(entry.SerializedEndpoint, Is.Null);
            Assert.That(entry.Signature, Is.Null);
        }

        [Test]
        public void PropertySetGet()
        {
            var serializedEndpoint = new byte[] { 0x1, 0x2 };
            var signature = new byte[] { 0x3, 0x4 };
            var entry = new AddressBookEntry()
            {
                SerializedEndpoint = serializedEndpoint,
                Signature = signature,
            };
            Assert.That(entry.SerializedEndpoint, Is.EqualTo(serializedEndpoint));
            Assert.That(entry.Signature, Is.EqualTo(signature));
        }

        [Test]
        public void Serializability()
        {
            var entry = new AddressBookEntry()
            {
                SerializedEndpoint = new byte[] { 0x1, 0x2 },
                Signature = new byte[] { 0x3, 0x4 },
            };

            var ms = new MemoryStream();
            var serializer = new DataContractSerializer(typeof(AddressBookEntry));
            serializer.WriteObject(ms, entry);
            ms.Position = 0;
            var deserializedEntry = (AddressBookEntry)serializer.ReadObject(ms);

            Assert.That(deserializedEntry.SerializedEndpoint, Is.EqualTo(entry.SerializedEndpoint));
            Assert.That(deserializedEntry.Signature, Is.EqualTo(entry.Signature));
        }

        [Test]
        public void ExtractEndpointWithoutCrypto()
        {
            var entry = new AddressBookEntry();
            Assert.Throws<ArgumentNullException>(() => entry.ExtractEndpoint());
        }

        [Test]
        public void ExtractEndpoint()
        {
            var ownContact = new OwnEndpoint(Valid.ReceivingEndpoint.SigningKey, Valid.ReceivingEndpoint.EncryptionKey);
            var cryptoServices = new CryptoSettings(SecurityLevel.Minimum);
            var entry = ownContact.CreateAddressBookEntry(cryptoServices);

            var endpoint = entry.ExtractEndpoint();
            Assert.That(endpoint, Is.EqualTo(ownContact.PublicEndpoint));
        }

        [Test]
        public void ExtractEndpointDetectsTampering()
        {
            var ownContact = Valid.GenerateOwnEndpoint(this.desktopCryptoProvider);
            var entry = ownContact.CreateAddressBookEntry(this.desktopCryptoProvider);

            var untamperedEndpoint = entry.SerializedEndpoint.CopyBuffer();
            for (int i = 0; i < 100; i++)
            {
                TestUtilities.ApplyFuzzing(entry.SerializedEndpoint, 1);
                Assert.Throws<BadAddressBookEntryException>(() => entry.ExtractEndpoint());
                untamperedEndpoint.CopyBuffer(entry.SerializedEndpoint);
            }
        }
    }
}
