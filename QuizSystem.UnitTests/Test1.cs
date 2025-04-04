using Backend_online_testing.Models;
using Backend_online_testing.Services;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DocumentFormat.OpenXml;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xceed.Words.NET;

namespace QuizSystem.UnitTests
{
    public class Test1
    {
        private readonly Mock<IMongoDatabase> _mockDatabase;
        private readonly Mock<IMongoCollection<SubjectsModel>> _mockSubjectsCollection;
        private readonly Mock<IMongoCollection<UsersModel>> _mockUsersCollection;
        private readonly Mock<AddLogService> _mockLogService;
        private readonly FileManagementService _service;
        public Test1()
        {
            _mockDatabase = new Mock<IMongoDatabase>();
            _mockSubjectsCollection = new Mock<IMongoCollection<SubjectsModel>>();
            _mockUsersCollection = new Mock<IMongoCollection<UsersModel>>();
            _mockLogService = new Mock<AddLogService>();

            _mockDatabase.Setup(db => db.GetCollection<SubjectsModel>("subjects", null))
                .Returns(_mockSubjectsCollection.Object);
            _mockDatabase.Setup(db => db.GetCollection<UsersModel>("users", null))
                .Returns(_mockUsersCollection.Object);

            _service = new FileManagementService(_mockDatabase.Object, _mockLogService.Object);
        }

        private StreamReader CreateDocxStreamReader(string filePath)
        {
            // Đọc file docx và chuyển thành StreamReader
            var doc = DocX.Load(filePath);
            var contentBuilder = new StringBuilder();

            foreach (var paragraph in doc.Paragraphs)
            {
                contentBuilder.AppendLine(paragraph.Text);
            }

            var stream = new MemoryStream(Encoding.UTF8.GetBytes(contentBuilder.ToString()));
            return new StreamReader(stream);
        }

        // Add this helper class to your test file
        public static class MockCursor<T>
        {
            public static IAsyncCursor<T> Create(List<T> items)
            {
                var mockCursor = new Mock<IAsyncCursor<T>>();
                mockCursor.Setup(_ => _.Current).Returns(items);
                mockCursor
                    .SetupSequence(_ => _.MoveNext(It.IsAny<CancellationToken>()))
                    .Returns(true)
                    .Returns(false);
                mockCursor
                    .SetupSequence(_ => _.MoveNextAsync(It.IsAny<CancellationToken>()))
                    .Returns(Task.FromResult(true))
                    .Returns(Task.FromResult(false));
                return mockCursor.Object;
            }

            public static IAsyncCursor<T> Empty => Create(new List<T>());
        }
        private string GetTestFilePath(string fileName)
        {
            var projectDir = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.FullName;
            return Path.Combine(projectDir, "TestFiles", fileName);
        }

        // TC01. Phân môn không hợp lệ
        [Fact]
        public async Task ProcessFileTxt_SubjectNotFound_ReturnsErrorMessage()
        {
            // Arrange
            var subjectId = "nonexistent_subject";
            var questionBankId = "test_bank";
            var filePath = GetTestFilePath("LS40_22KB.docx");
            var reader = CreateDocxStreamReader(filePath);

            _mockSubjectsCollection.Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<FindOptions<SubjectsModel, SubjectsModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(MockCursor<SubjectsModel>.Empty);

            // Act
            var result = await _service.ProcessFileTxt(reader, subjectId, questionBankId);

            // Assert
            Assert.Equal("Không tìm thấy phân môn", result);
        }

        // TC02. Ngân hàng câu hỏi không hợp lệ
        [Fact]
        public async Task ProcessFileTxt_QuestionBankNotFound_ReturnsErrorMessage()
        {
            // Arrange
            var subjectId = "valid_subject";
            var questionBankId = "nonexistent_bank";
            var filePath = GetTestFilePath("LS40_22KB.docx");
            await using var fileStream = File.OpenRead(filePath);

            var subject = new SubjectsModel
            {
                Id = subjectId,
                QuestionBanks = new List<QuestionBanksModel>
        {
            new QuestionBanksModel { QuestionBankId = "other_bank" }
        }
            };

            _mockSubjectsCollection.Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<FindOptions<SubjectsModel, SubjectsModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(MockCursor<SubjectsModel>.Create(new List<SubjectsModel> { subject }));

            // Act
            var result = await _service.ProcessFileDocx(fileStream, subjectId, questionBankId);

            // Assert
            Assert.Equal("Không tìm thấy bộ câu hỏi", result);
        }

        // TC03. Tải thành công file .txt dung lượng nhỏ
        [Fact]
        public async Task ProcessTxtFile_ValidFileSmallSize_ProcessesCorrectly()
        {
            // Arrange
            var subjectId = "valid_subject";
            var questionBankId = "valid_bank";

            var filePath = GetTestFilePath("ValidTxt.txt");
            using var reader = new StreamReader(filePath);

            var subject = new SubjectsModel
            {
                Id = subjectId,
                QuestionBanks = new List<QuestionBanksModel>
        {
            new QuestionBanksModel { QuestionBankId = questionBankId }
        }
            };

            _mockSubjectsCollection.Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<FindOptions<SubjectsModel, SubjectsModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(MockCursor<SubjectsModel>.Create(new List<SubjectsModel> { subject }));

            _mockSubjectsCollection.Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<UpdateDefinition<SubjectsModel>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            // Act
            var result = await _service.ProcessFileTxt(reader, subjectId, questionBankId);

            // Assert
            Assert.Equal("Tải tệp câu hỏi thành công", result);
        }

        // TC04. Tải thành công file .docx dung lượng lớn
        [Fact]
        public async Task ProcessDocxFile_ValidFileLargeSize_ProcessesCorrectly()
        {
            // Arrange
            var subjectId = "valid_subject";
            var questionBankId = "valid_bank";

            // Lấy đường dẫn tuyệt đối đến file test
            var solutionDir = Directory.GetParent(Directory.GetCurrentDirectory()).Parent.Parent.Parent.FullName;
            var filePath = Path.Combine(solutionDir, "QuizSystem.UnitTests", "TestFiles", "LS1_99mb.docx");

            await using var fileStream = File.OpenRead(filePath);

            var subject = new SubjectsModel
            {
                Id = subjectId,
                QuestionBanks = new List<QuestionBanksModel>
        {
            new QuestionBanksModel { QuestionBankId = questionBankId }
        }
            };

            _mockSubjectsCollection.Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<FindOptions<SubjectsModel, SubjectsModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(MockCursor<SubjectsModel>.Create(new List<SubjectsModel> { subject }));

            _mockSubjectsCollection.Setup(x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<UpdateDefinition<SubjectsModel>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new UpdateResult.Acknowledged(1, 1, null));

            // Act
            var result = await _service.ProcessFileDocx(fileStream, subjectId, questionBankId);

            // Assert
            Assert.Equal("Tải tệp câu hỏi thành công", result);
        }

        // TC05. Tải không thành công file .docx có ít hơn 2 lựa chọn đáp án
        [Fact]
        public async Task ProcessFileTxt_LessThanTwoOptions_ReturnsErrorMessage()
        {
            // Arrange
            var subjectId = "valid_subject";
            var questionBankId = "valid_bank";
            var filePath = GetTestFilePath("InvalidOptions2.docx");
            await using var fileStream = File.OpenRead(filePath);

            var subject = new SubjectsModel
            {
                Id = subjectId,
                QuestionBanks = new List<QuestionBanksModel>
        {
            new QuestionBanksModel { QuestionBankId = questionBankId }
        }
            };

            _mockSubjectsCollection.Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<FindOptions<SubjectsModel, SubjectsModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(MockCursor<SubjectsModel>.Create(new List<SubjectsModel> { subject }));

            // Act
            var result = await _service.ProcessFileDocx(fileStream, subjectId, questionBankId);

            // Assert
            Assert.Equal("Số lượng lựa chọn phải từ 2 đến 10.", result); // Kiểm tra thông báo lỗi
        }

        // TC06. Tải không thành công file .docx có 11 lựa chọn đáp án
        [Fact]
        public async Task ProcessFileTxt_MoreThanTenOptions_ReturnsErrorMessage()
        {
            // Arrange
            var subjectId = "valid_subject";
            var questionBankId = "valid_bank";

            var filePath = GetTestFilePath("InvalidOptions10.docx");
            await using var fileStream = File.OpenRead(filePath);

            var subject = new SubjectsModel
            {
                Id = subjectId,
                QuestionBanks = new List<QuestionBanksModel>
        {
            new QuestionBanksModel { QuestionBankId = questionBankId }
        }
            };

            _mockSubjectsCollection.Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<FindOptions<SubjectsModel, SubjectsModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(MockCursor<SubjectsModel>.Create(new List<SubjectsModel> { subject }));

            // Act
            var result = await _service.ProcessFileDocx(fileStream, subjectId, questionBankId);

            // Assert
            Assert.Equal("Số lượng lựa chọn phải từ 2 đến 10.", result);
        }

        // TC07. Kiểm thử tải lên file không có câu hỏi
        [Fact]
        public async Task ProcessDocxFile_NoQuestions_ReturnsErrorMessage()
        {
            // Arrange
            var filePath = GetTestFilePath("NoQuestions.docx");

            // Kiểm tra file tồn tại
            if (!File.Exists(filePath))
            {
                Assert.True(false, $"Test file not found: {filePath}");
                return;
            }

            await using var fileStream = File.OpenRead(filePath);

            var subject = new SubjectsModel
            {
                Id = "valid_subject",
                QuestionBanks = new List<QuestionBanksModel>
        {
            new QuestionBanksModel { QuestionBankId = "valid_bank" }
        }
            };

            _mockSubjectsCollection.Setup(x => x.FindAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<FindOptions<SubjectsModel, SubjectsModel>>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(MockCursor<SubjectsModel>.Create(new List<SubjectsModel> { subject }));

            // Act
            var result = await _service.ProcessFileDocx(fileStream, "valid_subject", "valid_bank");

            // Assert
            Assert.Equal("File không chứa câu hỏi nào hợp lệ", result);

            // Verify KHÔNG gọi update database
            _mockSubjectsCollection.Verify(
                x => x.UpdateOneAsync(
                    It.IsAny<FilterDefinition<SubjectsModel>>(),
                    It.IsAny<UpdateDefinition<SubjectsModel>>(),
                    It.IsAny<UpdateOptions>(),
                    It.IsAny<CancellationToken>()),
                Times.Never);
        }

        //Thêm sau khi kiểm thử bao phủ câu lệnh và nhánh
        [Fact]
        public async Task ProcessFileDocx_NullStream_ShouldThrowArgumentNullException()
        {
            // Arrange
            var subjectId = "subject1";
            var questionBankId = "bank1";

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(() =>
                _service.ProcessFileDocx(null, subjectId, questionBankId));

            Assert.Equal("fileStream", ex.ParamName);

        }

        [Fact]
        public async Task ProcessFileDocx_EmptyStream_ShouldThrowFileFormatException()
        {
            // Arrange
            var subjectId = "subject1";
            var questionBankId = "bank1";
            var emptyStream = new MemoryStream();

            // Act & Assert
            var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
                _service.ProcessFileDocx(emptyStream, subjectId, questionBankId));

            Assert.Equal("fileStream", ex.ParamName);
            Assert.Contains("cannot be empty", ex.Message);
        }

        [Fact]
        public async Task ProcessFileDocx_WhenBodyIsNull_ShouldReturnErrorMessage()
        {
            // Arrange
            var subjectId = "subject1";
            var questionBankId = "bank1";

            // Tạo stream chứa DOCX không có Body
            var stream = new MemoryStream();
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
            {
                var mainPart = doc.AddMainDocumentPart();
                mainPart.Document = new Document(); // Tạo Document nhưng không thêm Body
            }
            stream.Position = 0;

            // Act
            var result = await _service.ProcessFileDocx(stream, subjectId, questionBankId);

            // Assert
            Assert.Equal("File DOCX không có nội dung", result);
        }
    }
}
