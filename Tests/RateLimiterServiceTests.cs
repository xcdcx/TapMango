using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;
using Engine.Services;

namespace Tests
{
    [TestFixture]
    public class RateLimiterServiceTests
    {
        private const int MaxLimitPerNumber = 5;
        private const int MaxLimitPerAccount = 10;
        private Mock<ITransaction> _tranMock;
        private Mock<IConnectionMultiplexer> _redisMock;
        private Mock<IDatabase> _dbMock;
        private Mock<ILogger<RateLimiterService>> _loggerMock;
        private RateLimiterService _service;

        [SetUp]
        public void SetUp()
        {
            _redisMock = new Mock<IConnectionMultiplexer>();
            _dbMock = new Mock<IDatabase>();
            _tranMock = new Mock<ITransaction>();
            _loggerMock = new Mock<ILogger<RateLimiterService>>();

            _redisMock.Setup(x => x.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);


            _service = new RateLimiterService(
                _loggerMock.Object,
                _redisMock.Object,
                maxLimitPerNumber: 3,
                maxLimitPerAccount: 5);
        }

        [Test]
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task CanSendMessageAsync_Cooldown_ReturnsFalse(bool cool_down_by_number, bool cool_down_by_account)
        {
            // Arrange
            string phoneNumber = "1234567890";

            _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(s => s == $"cooldown:{phoneNumber}"), CommandFlags.None))
                .ReturnsAsync(cool_down_by_number ? RedisValue.EmptyString : RedisValue.Null);
            _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(s => s == "cooldown:account_limit"), CommandFlags.None))
                .ReturnsAsync(cool_down_by_account ? RedisValue.EmptyString : RedisValue.Null);
            // Act
            bool result = await _service.CanSendMessageAsync(phoneNumber);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public async Task CanSendMessageAsync_LimitNotExceeded_ReturnsTrue()
        {
            // Arrange
            string phoneNumber = "1234567890";

            _dbMock.Setup(db=>db.StringGetAsync(It.Is<RedisKey>(s => s == $"cooldown:{phoneNumber}"), CommandFlags.None))
                .ReturnsAsync(RedisValue.Null);
            _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(s => s == "cooldown:account_limit"), CommandFlags.None))
                .ReturnsAsync(RedisValue.Null);
            _dbMock.Setup(db => db.HashLengthAsync(It.Is<RedisKey>(s => s == $"sms_limit:{phoneNumber}"), CommandFlags.None))
                .ReturnsAsync(2);  // Below max limit for phone number
            _dbMock.Setup(db => db.HashLengthAsync(It.Is<RedisKey>(s => s == "account_limit"), CommandFlags.None))
                .ReturnsAsync(5);  // Below max limit for account

            _dbMock.Setup(x => x.CreateTransaction(It.IsAny<object>())).Returns(_tranMock.Object);
            _tranMock.Setup(x => x.AddCondition(It.IsAny<Condition>()));
            _tranMock.Setup(x => x.ExecuteAsync(CommandFlags.None)).ReturnsAsync(true);
            _tranMock.Setup(x => x.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()));

            // Act
            bool result = await _service.CanSendMessageAsync(phoneNumber);

            // Assert
            Assert.IsTrue(result);
        }

        [Test]
        public async Task CanSendMessageAsync_PhoneNumberLimitExceeded_ReturnsFalse()
        {
            // Arrange
            string phoneNumber = "1234567890";

            // Mock Redis behavior: No cooldown, exceeded limit for phone number
            _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(s => s == $"cooldown:{phoneNumber}"), CommandFlags.None))
                .ReturnsAsync(RedisValue.Null);
            _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(s => s == "cooldown:account_limit"), CommandFlags.None))
                .ReturnsAsync(RedisValue.Null);
            _dbMock.Setup(db => db.HashLengthAsync(It.Is<RedisKey>(s => s == $"sms_limit:{phoneNumber}"), CommandFlags.None))
                .ReturnsAsync(MaxLimitPerNumber);  // Exceeded limit for phone number
            _dbMock.Setup(db => db.HashLengthAsync(It.Is<RedisKey>(s => s == "account_limit"), CommandFlags.None))
                .ReturnsAsync(5);  // Below max limit for account

            _dbMock.Setup(x => x.CreateTransaction(It.IsAny<object>())).Returns(_tranMock.Object);
            _tranMock.Setup(x => x.AddCondition(It.IsAny<Condition>()));
            _tranMock.Setup(x => x.ExecuteAsync(CommandFlags.None)).ReturnsAsync(false);
            _tranMock.Setup(x => x.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()));
            // Act
            bool result = await _service.CanSendMessageAsync(phoneNumber);

            // Assert
            Assert.IsFalse(result);
        }

        [Test]
        public async Task CanSendMessageAsync_AccountLimitExceeded_ReturnsFalse()
        {
            // Arrange
            string phoneNumber = "1234567890";

            // Mock Redis behavior: No cooldown, exceeded limit for account
            _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(s => s == $"cooldown:{phoneNumber}"), CommandFlags.None))
                .ReturnsAsync(RedisValue.Null);
            _dbMock.Setup(db => db.StringGetAsync(It.Is<RedisKey>(s => s == "cooldown:account_limit"), CommandFlags.None))
                .ReturnsAsync(RedisValue.Null);
            _dbMock.Setup(db => db.HashLengthAsync(It.Is<RedisKey>(s => s == $"sms_limit:{phoneNumber}"), CommandFlags.None))
                .ReturnsAsync(3);  // Below max limit for phone number
            _dbMock.Setup(db => db.HashLengthAsync(It.Is<RedisKey>(s => s == "account_limit"), CommandFlags.None))
                .ReturnsAsync(MaxLimitPerAccount);  // Exceeded limit for account

            _dbMock.Setup(x => x.CreateTransaction(It.IsAny<object>())).Returns(_tranMock.Object);
            _tranMock.Setup(x => x.AddCondition(It.IsAny<Condition>()));
            _tranMock.Setup(x => x.ExecuteAsync(CommandFlags.None)).ReturnsAsync(false);
            _tranMock.Setup(x => x.HashSetAsync(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<RedisValue>(), It.IsAny<When>(), It.IsAny<CommandFlags>()));

            // Act
            bool result = await _service.CanSendMessageAsync(phoneNumber);

            // Assert
            Assert.IsFalse(result);
        }
    }
}