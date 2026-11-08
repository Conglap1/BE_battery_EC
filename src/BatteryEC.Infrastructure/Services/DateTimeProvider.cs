using BatteryEC.Application.Common.Interfaces;

namespace BatteryEC.Infrastructure.Services;

public class DateTimeProvider : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
