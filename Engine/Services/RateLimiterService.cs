using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Engine.Services;

public class RateLimiterService : IRateLimiterService
{
    private readonly ILogger<RateLimiterService> _logger;
    private readonly IDatabase _redisDB;
    private readonly int _maxLimitPerNumber;
    private readonly int _maxLimitPerAccount;
    private const string accountKey = "account_limit";

    public RateLimiterService(ILogger<RateLimiterService> logger, IConnectionMultiplexer redis, int maxLimitPerNumber, int maxLimitPerAccount)
    {
        _logger = logger;
        _redisDB = redis.GetDatabase();
        _maxLimitPerNumber = maxLimitPerNumber;
        _maxLimitPerAccount = maxLimitPerAccount;
    }

    public async Task<bool> CanSendMessageAsync(string phoneNumber)
    {
        var tranResult = await ExecuteLimitTransactionAsync(phoneNumber);
        _logger.LogInformation(tranResult ? $"Message allowed for phone number: {phoneNumber}" : $"Rate limit exceeded for phone number: {phoneNumber}");
        return tranResult;
    }

    private async Task<bool> ExecuteLimitTransactionAsync(string phoneNumber)
    {
        try
        {
            string now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
            string numberKey = $"sms_limit:{phoneNumber}";

            //atomic operation as a transaction
            var tran = _redisDB.CreateTransaction();
            tran.AddCondition(Condition.HashLengthLessThan(numberKey, _maxLimitPerNumber));
            tran.AddCondition(Condition.HashLengthLessThan(accountKey, _maxLimitPerAccount));

            tran.HashSetAsync(numberKey, now, "1");
            tran.KeyExpireAsync(numberKey, TimeSpan.FromSeconds(1));

            tran.HashSetAsync(accountKey, now, "1");
            tran.KeyExpireAsync(accountKey, TimeSpan.FromSeconds(1));

            return await tran.ExecuteAsync();
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, $"Redis error while processing request for phone number: {phoneNumber}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Unexpected error while processing request for phone number: {phoneNumber}");
            return false;
        }
    }
}
