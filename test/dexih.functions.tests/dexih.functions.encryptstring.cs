﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace dexih.functions.tests
{
    public class FunctionEncryptString
    {
        [Theory]
        [InlineData("a", "abc")]
        [InlineData("1a","abc")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "abc")]
        [InlineData("123123123123123", "abc")]
        [InlineData("123!@#$%^&*()_+-=`~\\|[]{};':\",./><?", "abc")]
        [InlineData("   ", "abc")]
        public void EncryptDecrypt(string TestValue, string Key)
        {
            //Use a for loop to similate gen sequence.
            var EncryptString1 = EncryptString.Encrypt(TestValue, Key, 1000);
            var EncryptString2 = EncryptString.Encrypt(TestValue, Key, 1000);
            Assert.True(EncryptString1.Success);
            Assert.True(EncryptString2.Success);
            Assert.NotEqual(EncryptString1.Value, EncryptString2.Value); //encryption is salted, so two encryptions should not be the same;

            //decrypt
            var DecryptString1 = EncryptString.Decrypt(EncryptString1.Value, Key, 1000);
            Assert.Equal(TestValue, DecryptString1.Value);

            //decypt with modified key.  should fail.
            var DecryptString2 = EncryptString.Decrypt(EncryptString1.Value, Key + " ", 1000);
            Assert.False(DecryptString2.Success);
            Assert.NotEqual(TestValue, DecryptString2.Value);
        }

        [Theory]
        [InlineData("123123123", "abc", 100000)]
        public void EncryptPerformance(string TestValue, string Key, int Iterations)
        {
            for(int i = 0; i< Iterations; i++)
            {
                var EncryptString1 = EncryptString.Encrypt(TestValue, Key, 5);
            }
        }
    }
}
