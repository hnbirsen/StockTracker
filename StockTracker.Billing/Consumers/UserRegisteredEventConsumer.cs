using MassTransit;
using StockTracker.Billing.Services;
using StockTracker.Shared.Contracts.Messages.V1;

namespace StockTracker.Billing.Consumers;

public class UserRegisteredEventConsumer : IConsumer<UserRegisteredEvent>
{
    private readonly IUserPlanService _userPlanService;
    private readonly ILogger<UserRegisteredEventConsumer> _logger;

    public UserRegisteredEventConsumer(IUserPlanService userPlanService, ILogger<UserRegisteredEventConsumer> logger)
    {
        _userPlanService = userPlanService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<UserRegisteredEvent> context)
    {
        await _userPlanService.AssignFreePlanAsync(context.Message.UserId);
        _logger.LogInformation("Free plan atandı — UserId: {UserId}", context.Message.UserId);
    }
}
