﻿#region Copyright notice and license

// Copyright 2019 The gRPC Authors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#endregion

using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using Grpc.AspNetCore.Web.Internal;
using Grpc.Tests.Shared;
using NUnit.Framework;

namespace Grpc.AspNetCore.Server.Tests.Web
{
    [TestFixture]
    public class Base64PipeReaderTests
    {
        [Test]
        public async Task ReadAsync_SmallData_Success()
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");
            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));
            var testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(base64Data);
            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var result = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert
            Assert.Greater(result.Buffer.Length, 0);

            CollectionAssert.AreEqual(initialData, result.Buffer.ToArray());
        }

        [Test]
        public async Task ReadAsync_MultipleWrites_Success()
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");
            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));
            var testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(base64Data.AsMemory(0, 2));
            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var resultTask = r.ReadAsync().AsTask().DefaultTimeout();

            Assert.IsFalse(resultTask.IsCompleted);

            await testPipe.Writer.WriteAsync(base64Data.AsMemory(2));

            var result = await resultTask;

            // Assert
            Assert.Greater(result.Buffer.Length, 0);

            CollectionAssert.AreEqual(initialData, result.Buffer.ToArray());
        }

        [Test]
        public async Task ReadAsync_ByteAtATime_Success()
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes("Hello world");
            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));
            var testPipe = new Pipe();
            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var resultTask = r.ReadAsync().AsTask().DefaultTimeout();

            Assert.IsFalse(resultTask.IsCompleted);

            for (int i = 0; i < base64Data.Length; i++)
            {
                await testPipe.Writer.WriteAsync(base64Data.AsMemory(i, 1));
                await Task.Delay(10);
            }

            var result = await resultTask;

            // Assert
            Assert.AreEqual(3, result.Buffer.Length);

            r.AdvanceTo(result.Buffer.Start, result.Buffer.End);

            result = await r.ReadAsync().AsTask().DefaultTimeout();

            CollectionAssert.AreEqual(initialData, result.Buffer.ToArray());
        }

        [TestCase("")]
        [TestCase("f")]
        [TestCase("fo")]
        [TestCase("foo")]
        [TestCase("foob")]
        [TestCase("fooba")]
        [TestCase("foobar")]
        [TestCase("The quick brown fox jumps over the lazy dog")]
        public async Task ReadAsync_RoundtripData_Success(string text)
        {
            // Arrange
            var initialData = Encoding.UTF8.GetBytes(text);

            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));

            var testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(base64Data).AsTask().DefaultTimeout();
            testPipe.Writer.Complete();

            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var result = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert
            CollectionAssert.AreEqual(initialData, result.Buffer.ToArray());
        }

        [Test]
        public async Task ReadAsync_MultipleBase64Fragements_Success()
        {
            // Arrange
            var base64Data = Encoding.UTF8.GetBytes("AAAAAAYKBHRlc3Q=gAAAABBncnBjLXN0YXR1czogMA0K");

            var testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(base64Data).AsTask().DefaultTimeout();
            testPipe.Writer.Complete();

            var r = new Base64PipeReader(testPipe.Reader);

            // Act 1
            var result1 = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert 1
            Assert.AreEqual("AAAAAAYKBHRlc3Q=", Convert.ToBase64String(result1.Buffer.ToArray()));

            // Act 2
            r.AdvanceTo(result1.Buffer.End);
            var result2 = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert 2
            Assert.AreEqual("gAAAABBncnBjLXN0YXR1czogMA0K", Convert.ToBase64String(result2.Buffer.ToArray()));

            // Act 3
            r.AdvanceTo(result2.Buffer.End);
            var result3 = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert 3
            Assert.IsTrue(result3.IsCompleted);
            Assert.AreEqual(0, result3.Buffer.Length);
        }

        [Test]
        public async Task ReadAsync_NotEnoughData_Error()
        {
            // Arrange
            var testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(new byte[] { (byte)'a' }).AsTask().DefaultTimeout();
            testPipe.Writer.Complete();

            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var ex = await ExceptionAssert.ThrowsAsync<InvalidOperationException>(() => r.ReadAsync().AsTask()).DefaultTimeout();

            // Assert
            Assert.AreEqual("Unexpected end of data when reading base64 content.", ex.Message);
        }

        [Test]
        public async Task CancelPendingRead_CancelOnFirstRead_CancelResult()
        {
            // Arrange
            var testPipe = new Pipe();

            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var readTask = r.ReadAsync().AsTask().DefaultTimeout();

            r.CancelPendingRead();

            var result = await readTask;

            // Assert
            Assert.IsTrue(result.IsCanceled);
        }

        [Test]
        public async Task CancelPendingRead_CancelOnSecondRead_CancelResult()
        {
            // Arrange
            var testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(new byte[] { (byte)'a' }).AsTask().DefaultTimeout();

            var r = new Base64PipeReader(testPipe.Reader);

            // Act
            var readTask = r.ReadAsync().AsTask().DefaultTimeout();

            r.CancelPendingRead();

            var result = await readTask;

            // Assert
            Assert.IsTrue(result.IsCanceled);
        }

        [Test]
        public async Task CancelPendingRead_CancelThenResumeReading_ReturnData()
        {
            // Arrange
            var testPipe = new Pipe();
            await testPipe.Writer.WriteAsync(new byte[] { (byte)'A' }).AsTask().DefaultTimeout();

            var r = new Base64PipeReader(testPipe.Reader);

            // Act 1
            var readTask = r.ReadAsync().AsTask().DefaultTimeout();

            r.CancelPendingRead();

            var result = await readTask.DefaultTimeout();

            // Assert 1
            Assert.IsTrue(result.IsCanceled);

            // Act 2
            r.AdvanceTo(result.Buffer.Start, result.Buffer.End);
            await testPipe.Writer.WriteAsync(new byte[] { (byte)'A', (byte)'=', (byte)'=' }).AsTask().DefaultTimeout();
            result = await r.ReadAsync().AsTask().DefaultTimeout();

            // Assert 2
            CollectionAssert.AreEqual(Convert.FromBase64String("AA=="), result.Buffer.ToArray());
        }

        [Test]
        public async Task ReadAsync_LargeMultipleBase64Fragements_Success()
        {
            // Arrange
            const int Messages = 10;

            var sb = new StringBuilder();
            for (var i = 0; i < 1000; i++)
            {
                sb.AppendLine("The quick brown fox jumped over the lazy dog.");
            }
            var initialData = Encoding.UTF8.GetBytes(sb.ToString());
            var base64Data = Encoding.UTF8.GetBytes(Convert.ToBase64String(initialData));

            var testPipe = new Pipe();

            var writeTask = Task.Run(async () =>
            {
                for (var i = 0; i < Messages; i++)
                {
                    await testPipe.Writer.WriteAsync(base64Data).AsTask().DefaultTimeout();
                }
                testPipe.Writer.Complete();
            });

            var r = new Base64PipeReader(testPipe.Reader);

            // Act & Assert
            var readMessages = 0;
            while (readMessages < Messages)
            {
                ReadResult result = default;
                while (result.Buffer.Length < initialData.Length)
                {
                    if (result.Buffer.Length > 0)
                    {
                        r.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                    }

                    result = await r.ReadAsync().AsTask().DefaultTimeout();
                }

                var message = result.Buffer.Slice(0, initialData.Length);

                CollectionAssert.AreEqual(initialData, message.ToArray());

                r.AdvanceTo(message.End);
                readMessages++;
            }

            var endResult = await r.ReadAsync().AsTask().DefaultTimeout();
            Assert.IsTrue(endResult.IsCompleted);
        }
    }
}
