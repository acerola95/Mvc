// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

#if ASPNET50
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Mvc.ModelBinding;
using Microsoft.AspNet.Mvc.Xml;
using Microsoft.AspNet.Testing;
using Moq;
using Xunit;
using Xunit.Sdk;

namespace Microsoft.AspNet.Mvc.Xml
{
    public class XmlSerializerInputFormatterTest
    {
        public class DummyClass
        {
            public int SampleInt { get; set; }
        }

        public class TestLevelOne
        {
            public int SampleInt { get; set; }
            public string sampleString;
            public DateTime SampleDate { get; set; }
        }

        public class TestLevelTwo
        {
            public string SampleString { get; set; }
            public TestLevelOne TestOne { get; set; }
        }

        private const string requiredErrorMessageFormat = "Property '{0}' on type '{1}' has" +
                                    " RequiredAttribute but no DataMember(IsRequired = true) attribute.";

        [Theory]
        [InlineData("application/xml", true)]
        [InlineData("application/*", true)]
        [InlineData("*/*", true)]
        [InlineData("text/xml", true)]
        [InlineData("text/*", true)]
        [InlineData("text/json", false)]
        [InlineData("application/json", false)]
        [InlineData("", false)]
        [InlineData("invalid", false)]
        [InlineData(null, false)]
        public void CanRead_ReturnsTrueForAnySupportedContentType(string requestContentType, bool expectedCanRead)
        {
            // Arrange
            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes("content");

            var actionContext = GetActionContext(contentBytes, contentType: requestContentType);
            var formatterContext = new InputFormatterContext(actionContext, typeof(string));

            // Act
            var result = formatter.CanRead(formatterContext);

            // Assert
            Assert.Equal(expectedCanRead, result);
        }

        [Fact]
        public void HasProperSuppportedMediaTypes()
        {
            // Arrange & Act
            var formatter = new XmlSerializerInputFormatter();

            // Assert
            Assert.True(formatter.SupportedMediaTypes
                                 .Select(content => content.ToString())
                                 .Contains("application/xml"));
            Assert.True(formatter.SupportedMediaTypes
                                 .Select(content => content.ToString())
                                 .Contains("text/xml"));
        }

        [Fact]
        public void HasProperSuppportedEncodings()
        {
            // Arrange & Act
            var formatter = new XmlSerializerInputFormatter();

            // Assert
            Assert.True(formatter.SupportedEncodings.Any(i => i.WebName == "utf-8"));
            Assert.True(formatter.SupportedEncodings.Any(i => i.WebName == "utf-16"));
        }

        [Fact]
        public async Task ReadAsync_ReadsSimpleTypes()
        {
            // Arrange
            var expectedInt = 10;
            var expectedString = "TestString";
            var expectedDateTime = XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc);

            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                                "<TestLevelOne><SampleInt>" + expectedInt + "</SampleInt>" +
                                "<sampleString>" + expectedString + "</sampleString>" +
                                "<SampleDate>" + expectedDateTime + "</SampleDate></TestLevelOne>";

            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelOne));

            // Act
            var model = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(model);
            Assert.IsType<TestLevelOne>(model);

            var levelOneModel = model as TestLevelOne;
            Assert.Equal(expectedInt, levelOneModel.SampleInt);
            Assert.Equal(expectedString, levelOneModel.sampleString);
            Assert.Equal(XmlConvert.ToDateTime(expectedDateTime, XmlDateTimeSerializationMode.Utc),
                         levelOneModel.SampleDate);
        }

        [Fact]
        public async Task ReadAsync_ReadsComplexTypes()
        {
            // Arrange
            var expectedInt = 10;
            var expectedString = "TestString";
            var expectedDateTime = XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc);
            var expectedLevelTwoString = "102";

            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<TestLevelTwo><SampleString>" + expectedLevelTwoString + "</SampleString>" +
                        "<TestOne><SampleInt>" + expectedInt + "</SampleInt>" +
                        "<sampleString>" + expectedString + "</sampleString>" +
                        "<SampleDate>" + expectedDateTime + "</SampleDate></TestOne></TestLevelTwo>";

            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act
            var model = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(model);
            Assert.IsType<TestLevelTwo>(model);

            var levelTwoModel = model as TestLevelTwo;
            Assert.Equal(expectedLevelTwoString, levelTwoModel.SampleString);
            Assert.Equal(expectedInt, levelTwoModel.TestOne.SampleInt);
            Assert.Equal(expectedString, levelTwoModel.TestOne.sampleString);
            Assert.Equal(XmlConvert.ToDateTime(expectedDateTime, XmlDateTimeSerializationMode.Utc),
                        levelTwoModel.TestOne.SampleDate);
        }

        [Fact]
        public async Task ReadAsync_ReadsWhenMaxDepthIsModified()
        {
            // Arrange
            var expectedInt = 10;

            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<DummyClass><SampleInt>" + expectedInt + "</SampleInt></DummyClass>";
            var formatter = new XmlSerializerInputFormatter();
            formatter.MaxDepth = 10;
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(DummyClass));


            // Act
            var model = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(model);
            Assert.IsType<DummyClass>(model);
            var dummyModel = model as DummyClass;
            Assert.Equal(expectedInt, dummyModel.SampleInt);
        }

        [Fact]
        public async Task ReadAsync_ThrowsOnExceededMaxDepth()
        {
            if (TestPlatformHelper.IsMono)
            {
                // ReaderQuotas are not honored on Mono
                return;
            }

            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<TestLevelTwo><SampleString>test</SampleString>" +
                        "<TestOne><SampleInt>10</SampleInt>" +
                        "<sampleString>test</sampleString>" +
                        "<SampleDate>" + XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc)
                        + "</SampleDate></TestOne></TestLevelTwo>";
            var formatter = new XmlSerializerInputFormatter();
            formatter.MaxDepth = 1;
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act & Assert
            await Assert.ThrowsAsync(typeof(InvalidOperationException), () => formatter.ReadAsync(context));
        }

        [Fact]
        public async Task ReadAsync_ThrowsWhenReaderQuotasAreChanged()
        {
            if (TestPlatformHelper.IsMono)
            {
                // ReaderQuotas are not honored on Mono
                return;
            }

            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                        "<TestLevelTwo><SampleString>test</SampleString>" +
                        "<TestOne><SampleInt>10</SampleInt>" +
                        "<sampleString>test</sampleString>" +
                        "<SampleDate>" + XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc)
                        + "</SampleDate></TestOne></TestLevelTwo>";
            var formatter = new XmlSerializerInputFormatter();
            formatter.XmlDictionaryReaderQuotas.MaxStringContentLength = 10;
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act & Assert
            await Assert.ThrowsAsync(typeof(InvalidOperationException), () => formatter.ReadAsync(context));
        }

        [Fact]
        public void SetMaxDepth_ThrowsWhenMaxDepthIsBelowOne()
        {
            // Arrange
            var formatter = new XmlSerializerInputFormatter();

            // Act & Assert
            Assert.Throws(typeof(ArgumentException), () => formatter.MaxDepth = 0);
        }

        [Fact]
        public async Task ReadAsync_VerifyStreamIsOpenAfterRead()
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<DummyClass><SampleInt>10</SampleInt></DummyClass>";
            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encoding.UTF8.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(DummyClass));

            // Act
            var model = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(model);
            Assert.True(context.ActionContext.HttpContext.Request.Body.CanRead);
        }

        [Fact]
        public async Task ReadAsync_ThrowsOnInvalidCharacters()
        {
            // Arrange
            var expectedException = TestPlatformHelper.IsMono ? typeof(InvalidOperationException) :
                                                                typeof(XmlException);
            var expectedMessage = TestPlatformHelper.IsMono ?
                "There is an error in XML document." :
                "The encoding in the declaration 'UTF-8' does not match the encoding of the document 'utf-16LE'.";

            var inpStart = Encodings.UTF16EncodingLittleEndian.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                "<DummyClass><SampleInt>");
            byte[] inp = { 192, 193 };
            var inpEnd = Encodings.UTF16EncodingLittleEndian.GetBytes("</SampleInt></DummyClass>");

            var contentBytes = new byte[inpStart.Length + inp.Length + inpEnd.Length];
            Buffer.BlockCopy(inpStart, 0, contentBytes, 0, inpStart.Length);
            Buffer.BlockCopy(inp, 0, contentBytes, inpStart.Length, inp.Length);
            Buffer.BlockCopy(inpEnd, 0, contentBytes, inpStart.Length + inp.Length, inpEnd.Length);

            var formatter = new XmlSerializerInputFormatter();
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act and Assert
            var ex = await Assert.ThrowsAsync(expectedException, () => formatter.ReadAsync(context));
            Assert.Equal(expectedMessage, ex.Message);
        }

        [Fact]
        public async Task ReadAsync_IgnoresBOMCharacters()
        {
            // Arrange
            var sampleString = "Test";
            var sampleStringBytes = Encoding.UTF8.GetBytes(sampleString);
            var inputStart = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine +
                "<TestLevelTwo><SampleString>" + sampleString);
            byte[] bom = { 0xef, 0xbb, 0xbf };
            var inputEnd = Encoding.UTF8.GetBytes("</SampleString></TestLevelTwo>");
            var expectedBytes = new byte[sampleString.Length + bom.Length];

            var contentBytes = new byte[inputStart.Length + bom.Length + inputEnd.Length];
            Buffer.BlockCopy(inputStart, 0, contentBytes, 0, inputStart.Length);
            Buffer.BlockCopy(bom, 0, contentBytes, inputStart.Length, bom.Length);
            Buffer.BlockCopy(inputEnd, 0, contentBytes, inputStart.Length + bom.Length, inputEnd.Length);

            var formatter = new XmlSerializerInputFormatter();
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelTwo));

            // Act
            var model = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(model);
            var levelTwoModel = model as TestLevelTwo;
            Buffer.BlockCopy(sampleStringBytes, 0, expectedBytes, 0, sampleStringBytes.Length);
            Buffer.BlockCopy(bom, 0, expectedBytes, sampleStringBytes.Length, bom.Length);
            Assert.Equal(expectedBytes, Encoding.UTF8.GetBytes(levelTwoModel.SampleString));
        }

        [Fact]
        public async Task ReadAsync_AcceptsUTF16Characters()
        {
            // Arrange
            var expectedInt = 10;
            var expectedString = "TestString";
            var expectedDateTime = XmlConvert.ToString(DateTime.UtcNow, XmlDateTimeSerializationMode.Utc);

            var input = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
                                "<TestLevelOne><SampleInt>" + expectedInt + "</SampleInt>" +
                                "<sampleString>" + expectedString + "</sampleString>" +
                                "<SampleDate>" + expectedDateTime + "</SampleDate></TestLevelOne>";

            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encodings.UTF16EncodingLittleEndian.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(TestLevelOne));

            // Act
            var model = await formatter.ReadAsync(context);

            // Assert
            Assert.NotNull(model);
            Assert.IsType<TestLevelOne>(model);

            var levelOneModel = model as TestLevelOne;
            Assert.Equal(expectedInt, levelOneModel.SampleInt);
            Assert.Equal(expectedString, levelOneModel.sampleString);
            Assert.Equal(XmlConvert.ToDateTime(expectedDateTime, XmlDateTimeSerializationMode.Utc), levelOneModel.SampleDate);
        }

        [Fact]
        public async Task ModelHavingRequiredErrors()
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                        "<Address xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                        "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><IsResidential>" +
                        "true</IsResidential><Zipcode>98052</Zipcode></Address>";
            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encodings.UTF8EncodingWithoutBOM.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(Address));

            // Act
            var model = await formatter.ReadAsync(context) as Address;

            // Assert
            Assert.NotNull(model);
            Assert.Equal(98052, model.Zipcode);
            Assert.Equal(true, model.IsResidential);
            Assert.NotEmpty(context.ActionContext.ModelState);

            ModelState modelState;
            context.ActionContext.ModelState.TryGetValue(typeof(Address).FullName, out modelState);
            Assert.NotNull(modelState);
            Assert.NotEmpty(modelState.Errors);

            var errors = modelState.Errors.Select(error =>
            {
                if (string.IsNullOrEmpty(error.ErrorMessage))
                {
                    if (error.Exception != null)
                    {
                        return error.Exception.Message;
                    }
                }

                return error.ErrorMessage;
            });

            Assert.Contains<string>(
                string.Format(requiredErrorMessageFormat, nameof(Address.Zipcode), typeof(Address).FullName),
                errors);
            Assert.Contains<string>(
                string.Format(requiredErrorMessageFormat, nameof(Address.IsResidential), typeof(Address).FullName),
                errors);
        }

        [Fact]
        public async Task AddsRequiredErrors2()
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"utf-8\"?><ModelWithPropertyHavingRequiredErrors " +
                "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Property1><Zipcode>98052</Zipcode><IsResidential>" +
                "true</IsResidential></Property1></ModelWithPropertyHavingRequiredErrors>";
            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encodings.UTF8EncodingWithoutBOM.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(ModelWithPropertyHavingRequiredErrors));

            // Act
            var model = await formatter.ReadAsync(context) as ModelWithPropertyHavingRequiredErrors;

            // Assert
            Assert.NotNull(model);
            Assert.NotNull(model.Property1);
            Assert.Equal(98052, model.Property1.Zipcode);
            Assert.Equal(true, model.Property1.IsResidential);
            Assert.NotEmpty(context.ActionContext.ModelState);

            ModelState modelState;
            context.ActionContext.ModelState.TryGetValue(typeof(Address).FullName, out modelState);
            Assert.NotNull(modelState);
            Assert.NotEmpty(modelState.Errors);

            var errors = modelState.Errors.Select(error =>
            {
                if (string.IsNullOrEmpty(error.ErrorMessage))
                {
                    if (error.Exception != null)
                    {
                        return error.Exception.Message;
                    }
                }

                return error.ErrorMessage;
            });

            Assert.Contains<string>(
                string.Format(requiredErrorMessageFormat, nameof(Address.Zipcode), typeof(Address).FullName),
                errors);
            Assert.Contains<string>(
                string.Format(requiredErrorMessageFormat, nameof(Address.IsResidential), typeof(Address).FullName),
                errors);
        }

        [Fact]
        public async Task AddsRequiredErrors3()
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"utf-8\"?><ModelWithCollectionPropertyHavingRequiredErrors" +
                " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Property1><Address><Zipcode>98052</Zipcode>" +
                "<IsResidential>true</IsResidential></Address></Property1>" +
                "</ModelWithCollectionPropertyHavingRequiredErrors>";
            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encodings.UTF8EncodingWithoutBOM.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(ModelWithCollectionPropertyHavingRequiredErrors));

            // Act
            var model = await formatter.ReadAsync(context) as ModelWithCollectionPropertyHavingRequiredErrors;

            // Assert
            Assert.NotNull(model);
            Assert.NotNull(model.Property1);
            Assert.Equal(98052, model.Property1[0].Zipcode);
            Assert.Equal(true, model.Property1[0].IsResidential);
            Assert.NotEmpty(context.ActionContext.ModelState);

            ModelState modelState;
            context.ActionContext.ModelState.TryGetValue(typeof(Address).FullName, out modelState);
            Assert.NotNull(modelState);
            Assert.NotEmpty(modelState.Errors);

            var errors = modelState.Errors.Select(error =>
            {
                if (string.IsNullOrEmpty(error.ErrorMessage))
                {
                    if (error.Exception != null)
                    {
                        return error.Exception.Message;
                    }
                }

                return error.ErrorMessage;
            });

            Assert.Contains<string>(
                string.Format(requiredErrorMessageFormat, nameof(Address.Zipcode), typeof(Address).FullName),
                errors);
            Assert.Contains<string>(
                string.Format(requiredErrorMessageFormat, nameof(Address.IsResidential), typeof(Address).FullName),
                errors);
        }

        [Fact]
        public async Task ModelInheritingTypeHavingRequiredErrors_AddsRequiredErrors()
        {
            // Arrange
            var input = "<?xml version=\"1.0\" encoding=\"utf-8\"?><ModelInheritingTypeHavingRequiredErrors" +
                " xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\"><Zipcode>98052</Zipcode><IsResidential>true" + 
                "</IsResidential></ModelInheritingTypeHavingRequiredErrors>";
            var formatter = new XmlSerializerInputFormatter();
            var contentBytes = Encodings.UTF8EncodingWithoutBOM.GetBytes(input);
            var context = GetInputFormatterContext(contentBytes, typeof(ModelInheritingTypeHavingRequiredErrors));

            // Act
            var model = await formatter.ReadAsync(context) as ModelInheritingTypeHavingRequiredErrors;

            // Assert
            Assert.NotNull(model);
            Assert.Equal(98052, model.Zipcode);
            Assert.Equal(true, model.IsResidential);
            Assert.NotEmpty(context.ActionContext.ModelState);

            ModelState modelState;
            context.ActionContext.ModelState.TryGetValue(typeof(Address).FullName, out modelState);
            Assert.NotNull(modelState);
            Assert.NotEmpty(modelState.Errors);

            var errors = modelState.Errors.Select(error =>
            {
                if (string.IsNullOrEmpty(error.ErrorMessage))
                {
                    if (error.Exception != null)
                    {
                        return error.Exception.Message;
                    }
                }

                return error.ErrorMessage;
            });

            Assert.Contains<string>(
                string.Format(requiredErrorMessageFormat, nameof(Address.Zipcode), typeof(Address).FullName),
                errors);
            Assert.Contains<string>(
                string.Format(requiredErrorMessageFormat, nameof(Address.IsResidential), typeof(Address).FullName),
                errors);
        }

        private InputFormatterContext GetInputFormatterContext(byte[] contentBytes, Type modelType)
        {
            var actionContext = GetActionContext(contentBytes);
            var metadata = new EmptyModelMetadataProvider().GetMetadataForType(null, modelType);
            return new InputFormatterContext(actionContext, metadata.ModelType);
        }

        private static ActionContext GetActionContext(byte[] contentBytes,
                                                      string contentType = "application/xml")
        {
            return new ActionContext(GetHttpContext(contentBytes, contentType),
                                     new AspNet.Routing.RouteData(),
                                     new ActionDescriptor());
        }
        private static HttpContext GetHttpContext(byte[] contentBytes,
                                                  string contentType = "application/xml")
        {
            var request = new Mock<HttpRequest>();
            var headers = new Mock<IHeaderDictionary>();
            request.SetupGet(r => r.Headers).Returns(headers.Object);
            request.SetupGet(f => f.Body).Returns(new MemoryStream(contentBytes));
            request.SetupGet(f => f.ContentType).Returns(contentType);

            var httpContext = new Mock<HttpContext>();
            httpContext.SetupGet(c => c.Request).Returns(request.Object);
            httpContext.SetupGet(c => c.Request).Returns(request.Object);
            return httpContext.Object;
        }

        public class ModelWithPropertyHavingRequiredErrors
        {
            public Address Property1 { get; set; }
        }

        public class ModelWithCollectionPropertyHavingRequiredErrors
        {
            public List<Address> Property1 { get; set; }
        }

        public class ModelInheritingTypeHavingRequiredErrors : Address
        {
        }
    }
}
#endif
