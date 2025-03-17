using Engine.Services;
using Microsoft.Extensions.Logging;
using Moq;
using StackExchange.Redis;

namespace Tests
{
    [TestFixture]
    public class RateLimiteServicerIntegrationTests
    {
        private RateLimiterService _service;
        private IConnectionMultiplexer _redis;
        private Mock<ILogger<RateLimiterService>> _mockLogger;
        private readonly int _maxPerNumber = 3;
        private readonly int _maxPerAccount = 5;

        [SetUp]
        public async Task SetUp()
        {
            _mockLogger = new Mock<ILogger<RateLimiterService>>();
            _redis = await ConnectionMultiplexer.ConnectAsync("Localhost:6379");

            //Clean up redis before each test
            await _redis.GetDatabase().ExecuteAsync("FLUSHDB");

            _service = new RateLimiterService(_mockLogger.Object, _redis, _maxPerNumber, _maxPerAccount);
        }

        [TearDown]
        public async Task TearDown()
        {
            await _redis.GetDatabase().ExecuteAsync("FLUSHDB");
            _redis.Dispose();
        }

        [Test]
        public async Task CanSendMessageAsynv_WhenBelowLimit_ShouldReturnTrue()
        {
            var result = await _service.CanSendMessageAsync("123456789");
            Assert.IsTrue(result, "Should allow message when below limit");
        }

        [Test]
        public async Task CanSendMessageAsync_WhenLimitPerNumberExceeded_ShouldReturnFalse()
        {
            //send 3 messages (within limit)
            await _service.CanSendMessageAsync("123456789");
            await _service.CanSendMessageAsync("123456789");
            await _service.CanSendMessageAsync("123456789");

            //4th message shold fail due to number limit
            var result = await _service.CanSendMessageAsync("123456789");
            Assert.IsFalse(result, "Should block message after exceeding number limit");
        }

        [Test]
        public async Task CanSendMessageAsync_WhenLimitPerAccountExceeded_ShouldReturnFalse()
        {
            //Send 5 messages from different numbers
            await _service.CanSendMessageAsync("111111111");
            await _service.CanSendMessageAsync("222222222");
            await _service.CanSendMessageAsync("333333333");
            await _service.CanSendMessageAsync("444444444");
            await _service.CanSendMessageAsync("555555555");

            //6th message should fail due account limit
            var result = await _service.CanSendMessageAsync("666666666");
            Assert.IsFalse(result, "Should block message after exceeding account limit");
        }

        [Test]
        public async Task CanSendMessageAsync_ShouldRespectCoolDownPerAccount()
        {
            //Send up to account limit
            await _service.CanSendMessageAsync("111111111");
            await _service.CanSendMessageAsync("222222222");
            await _service.CanSendMessageAsync("333333333");
            await _service.CanSendMessageAsync("444444444");
            await _service.CanSendMessageAsync("555555555");

            //6th message should fail due to account limit
            var result = await _service.CanSendMessageAsync("666666666");
            Assert.IsFalse(result, "Should block message after exceeding account limit");

            //Cooldown 1 sec by a the service logic
            await Task.Delay(1000);

            //Should succeed after cooldown
            result = await _service.CanSendMessageAsync("666666666");
            Assert.IsTrue(result, "Should alow message after account cooldown");
        }

        [Test]
        public async Task CanSendMessageAsync_ShouldRespectCoolDownPerNumber()
        {
            //Exceed number limit
            await _service.CanSendMessageAsync("111111111");
            await _service.CanSendMessageAsync("111111111");
            await _service.CanSendMessageAsync("111111111");

            //4th request should fail
            var result = await _service.CanSendMessageAsync("111111111");
            Assert.IsFalse(result, "Should block the message after exceeding number limit");

            //Service cooldown 1 sec
            await Task.Delay(1000);

            //Should succeed after cooldown
            result = await _service.CanSendMessageAsync("111111111");
            Assert.IsTrue(result, "Shold allow message after cooldown");
        }
    }
}
