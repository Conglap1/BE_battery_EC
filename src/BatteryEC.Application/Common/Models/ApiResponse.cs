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

    public static ApiResponse<T> SuccessResult(T data, string? message = null) => Ok(data, message);
    public static ApiResponse<T> FailureResult(string error, string? message = null) => Fail(error, message);
    public static ApiResponse<T> FailureResult(IEnumerable<string> errors, string? message = null) => Fail(errors, message);
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

    public static ApiResponse SuccessResult(string? message = null) => Ok(message);
    public static new ApiResponse FailureResult(string error, string? message = null) => Fail(error, message);
}
