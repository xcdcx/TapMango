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

            // Check cooldown for phone number
            if (await _redisDB.StringGetAsync($"cooldown:{phoneNumber}") != RedisValue.Null)
            {
                _logger.LogWarning($"Phone number {phoneNumber} is in cooldown period.");
                return false;
            }

            // Check cooldown for account
            if (await _redisDB.StringGetAsync($"cooldown:{accountKey}") != RedisValue.Null)
            {
                _logger.LogWarning($"Account is in cooldown period.");
                return false;
            }

            //atomic operation as a transaction
            var tran = _redisDB.CreateTransaction();
            tran.AddCondition(Condition.HashLengthLessThan(numberKey, _maxLimitPerNumber));
            tran.AddCondition(Condition.HashLengthLessThan(accountKey, _maxLimitPerAccount));

            _ = tran.HashSetAsync(numberKey, now, "1");
            _ = tran.KeyExpireAsync(numberKey, TimeSpan.FromSeconds(1));

            _ = tran.HashSetAsync(accountKey, now, "1");
            _ = tran.KeyExpireAsync(accountKey, TimeSpan.FromSeconds(1));

            bool tranResult = await tran.ExecuteAsync();

            if (!tranResult)
            {
                // Handle exceeded limits
                if (await _redisDB.HashLengthAsync(accountKey) >= _maxLimitPerAccount)
                {
                    _logger.LogWarning($"Account limit exceeded. Setting account cooldown.");
                    await _redisDB.StringSetAsync($"cooldown:{accountKey}", "1", TimeSpan.FromSeconds(1));
                }
                else if (await _redisDB.HashLengthAsync(numberKey) >= _maxLimitPerNumber)
                {
                    _logger.LogWarning($"Number limit exceeded for {phoneNumber}. Setting number cooldown.");
                    await _redisDB.StringSetAsync($"cooldown:{phoneNumber}", "1", TimeSpan.FromSeconds(1));
                }
            }

            return tranResult;
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
