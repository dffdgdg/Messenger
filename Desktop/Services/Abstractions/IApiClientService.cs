using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop.Services.Abstractions;

public interface IApiClientService : IDisposable
{
    Task<ApiResponse<T>> GetAsync<T>(string url, CancellationToken ct = default);
    Task<ApiResponse<TResponse>> PostAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken ct = default);
    Task<ApiResponse<T>> PostAsync<T>(string url, object data, CancellationToken ct = default);
    Task<ApiResponse<object>> PostAsync(string url, object? data, CancellationToken ct = default);
    Task<ApiResponse<T>> PutAsync<T>(string url, object data, CancellationToken ct = default);
    Task<ApiResponse<TResponse>> PutAsync<TRequest, TResponse>(string url, TRequest data, CancellationToken ct = default);
    Task<ApiResponse<object>> PutAsync(string url, object data, CancellationToken ct = default);
    Task<ApiResponse<object>> DeleteAsync(string url, CancellationToken ct = default);
    Task<ApiResponse<T>> DeleteAsync<T>(string url, CancellationToken ct = default);
    Task<ApiResponse<T>> UploadFileAsync<T>(string url, Stream fileStream, string fileName, string contentType, CancellationToken ct = default);
    Task<Stream?> GetStreamAsync(string url, CancellationToken ct = default);
}