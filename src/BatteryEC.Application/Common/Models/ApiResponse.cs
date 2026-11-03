namespace BatteryEC.Application.Common.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }
    public List<string> Errors { get; set; } = new();

    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message
    };

    public static ApiResponse<T> Fail(string error, string? message = null) => new()
    {
        Success = false,
        Message = message,
        Errors = new List<string> { error }
    };

    public static ApiResponse<T> Fail(IEnumerable<string> errors, string? message = null) => new()
    {
        Success = false,
        Message = message,
        Errors = errors.ToList()
    };
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        Message = message
    };

    public static new ApiResponse Fail(string error, string? message = null) => new()
    {
        Success = false,
        Message = message,
        Errors = new List<string> { error }
    };
}
