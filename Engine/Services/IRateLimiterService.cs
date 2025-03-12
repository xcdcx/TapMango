namespace Engine.Services
{
    public interface IRateLimiterService
    {
        public Task<bool> CanSendMessageAsync(string phoneNumber);
    }
}
