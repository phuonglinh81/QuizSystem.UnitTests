using Backend_online_testing.Controllers;
using Backend_online_testing.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QuizSystem.UnitTests
{
    public class Test3
    {
        private readonly Mock<IFileManagementService> _mockFileService;
        private readonly FileManagementController _controller;

        public Test3()
        {
            _mockFileService = new Mock<IFileManagementService>();
            _controller = new FileManagementController(_mockFileService.Object);
        }

        // Helper method to create mock IFormFile
        private IFormFile CreateMockFormFile(string fileName, string content, string contentType)
        {
            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(content);
            writer.Flush();
            stream.Position = 0;

            return new FormFile(stream, 0, stream.Length, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };
        }

        [Fact]
        public async Task UploadFile_NoFile_ReturnsBadRequest()
        {
            // Arrange
            IFormFile file = null;
            var subjectId = "subject1";
            var questionBankId = "bank1";

            // Act
            var result = await _controller.UploadFile(file, subjectId, questionBankId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Vui lòng chọn file hợp lệ.", badRequestResult.Value);
        }

        [Fact]
        public async Task UploadFile_EmptyFile_ReturnsBadRequest()
        {
            // Arrange
            var file = new Mock<IFormFile>();
            file.Setup(f => f.Length).Returns(0);
            file.Setup(f => f.FileName).Returns("test.txt");
            var subjectId = "subject1";
            var questionBankId = "bank1";

            // Act
            var result = await _controller.UploadFile(file.Object, subjectId, questionBankId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Vui lòng chọn file hợp lệ.", badRequestResult.Value);
        }

        [Fact]
        public async Task UploadFile_FileTooLarge_ReturnsBadRequest()
        {
            // Arrange
            var file = new Mock<IFormFile>();
            file.Setup(f => f.Length).Returns(4 * 1024 * 1024); // 4MB
            file.Setup(f => f.FileName).Returns("LS7000_5_13.docx");
            var subjectId = "subject1";
            var questionBankId = "bank1";

            // Act
            var result = await _controller.UploadFile(file.Object, subjectId, questionBankId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Kích thước file không được vượt quá 2MB.", badRequestResult.Value);
        }

        [Fact]
        public async Task UploadFile_UnsupportedFileType_ReturnsBadRequest()
        {
            // Arrange
            var file = CreateMockFormFile("test.xlsx", "test content", "application/pdf");
            var subjectId = "subject1";
            var questionBankId = "bank1";

            // Act
            var result = await _controller.UploadFile(file, subjectId, questionBankId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Equal("Chỉ hỗ trợ file .txt và .docx.", badRequestResult.Value);
        }

        [Fact]
        public async Task UploadFile_ValidTxtFile_ReturnsOk()
        {
            // Arrange
            var file = CreateMockFormFile("ValidTxt.txt", "test content", "text/plain");
            var subjectId = "subject1";
            var questionBankId = "bank1";

            _mockFileService.Setup(s => s.ProcessFileTxt(It.IsAny<StreamReader>(), subjectId, questionBankId))
                           .ReturnsAsync("Tải tệp câu hỏi thành công");

            // Act
            var result = await _controller.UploadFile(file, subjectId, questionBankId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            // Dùng reflection để kiểm tra property
            var valueType = okResult.Value.GetType();
            var messageProperty = valueType.GetProperty("message") ?? valueType.GetProperty("Message");

            Assert.NotNull(messageProperty); // Đảm bảo property tồn tại
            var messageValue = messageProperty.GetValue(okResult.Value) as string;
            Assert.Equal("Tải tệp câu hỏi thành công", messageValue);
        }

        [Fact]
        public async Task UploadFile_ValidDocxFile_ReturnsOk()
        {
            // Arrange
            var file = CreateMockFormFile("LS40_22KB.docx", "test content", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
            var subjectId = "subject1";
            var questionBankId = "bank1";

            _mockFileService.Setup(s => s.ProcessFileDocx(It.IsAny<Stream>(), subjectId, questionBankId))
                           .ReturnsAsync("Tải tệp câu hỏi thành công");

            // Act
            var result = await _controller.UploadFile(file, subjectId, questionBankId);

            // Assert
            var okResult = Assert.IsType<OkObjectResult>(result);
            // Dùng reflection để kiểm tra property
            var valueType = okResult.Value.GetType();
            var messageProperty = valueType.GetProperty("message") ?? valueType.GetProperty("Message");

            Assert.NotNull(messageProperty); // Đảm bảo property tồn tại
            var messageValue = messageProperty.GetValue(okResult.Value) as string;
            Assert.Equal("Tải tệp câu hỏi thành công", messageValue);
        }

        [Fact]
        public async Task UploadFile_FileProcessingFails_ReturnsBadRequest()
        {
            // Arrange
            var file = CreateMockFormFile("test.txt", "test content", "text/plain");
            var subjectId = "subject1";
            var questionBankId = "bank1";
            var errorMessage = "Invalid file format";

            _mockFileService.Setup(s => s.ProcessFileTxt(It.IsAny<StreamReader>(), subjectId, questionBankId))
                           .ReturnsAsync(errorMessage);

            // Act
            var result = await _controller.UploadFile(file, subjectId, questionBankId);

            // Assert
            var badRequestResult = Assert.IsType<BadRequestObjectResult>(result);
            var expected = JObject.FromObject(new { result = errorMessage });
            var actual = JObject.FromObject(badRequestResult.Value);
            Assert.Equal(expected.ToString(), actual.ToString());
        }

        [Fact]
        public async Task UploadFile_ThrowsException_ReturnsInternalServerError()
        {
            // Arrange
            var file = CreateMockFormFile("test.txt", "test content", "text/plain");
            var subjectId = "subject1";
            var questionBankId = "bank1";

            _mockFileService.Setup(s => s.ProcessFileTxt(It.IsAny<StreamReader>(), subjectId, questionBankId))
                           .ThrowsAsync(new Exception("Processing failed"));

            // Act
            var result = await _controller.UploadFile(file, subjectId, questionBankId);

            // Assert
            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusCodeResult.StatusCode);

            // Use reflection to access properties
            var valueType = statusCodeResult.Value.GetType();
            var errorProperty = valueType.GetProperty("error");
            Assert.NotNull(errorProperty);
            Assert.Equal("FileProcessingError", errorProperty.GetValue(statusCodeResult.Value));
        }
    }
}
