using MessengerDesktop.Services.Audio;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

/// <summary> <param name="cancelReply">
/// ReplyHandler нужен для CancelReply после отправки голосового.
/// Передаётся как Func для избежания циклических зависимостей.
/// </param> </summary>
public sealed partial class ChatVoiceHandler(ChatContext context, Action cancelReply) : ChatFeatureHandler(context)
{
    private IAudioRecorderService _audioRecorder = null!;
    private CancellationTokenSource? _voiceSendCts;
    private CancellationTokenSource? _autoStopCts;

    private const double MinDuration = 0.5;
    private const double MaxDuration = 300;

    [ObservableProperty] public partial bool IsVoiceRecording { get; set; }
    [ObservableProperty] public partial bool IsVoiceSending { get; set; }
    [ObservableProperty] public partial string VoiceElapsed { get; set; } = "0:00";
    [ObservableProperty] public partial string? VoiceError { get; set; }
    [ObservableProperty] public partial bool IsVoiceSupported { get; set; }

    private VoiceRecordingViewModel? _voiceRecording;
    public VoiceRecordingViewModel? VoiceRecording
    {
        get => _voiceRecording;
        private set => SetProperty(ref _voiceRecording, value);
    }

    public void Initialize(IAudioRecorderService audioRecorder)
    {
        _audioRecorder = audioRecorder;
        IsVoiceSupported = _audioRecorder.IsSupported;
    }

    [RelayCommand]
    private async Task StartRecording()
    {
        if (IsVoiceRecording || IsVoiceSending) return;

        if (!_audioRecorder.IsSupported)
        {
            VoiceError = "Запись аудио не поддерживается на этой платформе";
            return;
        }

        VoiceError = null;
        var started = await _audioRecorder.StartAsync();

        if (!started)
        {
            VoiceError = "Не удалось начать запись. Проверьте микрофон.";
            return;
        }

        _voiceRecording?.Dispose();
        VoiceRecording = new VoiceRecordingViewModel(_audioRecorder)
        { State = AudioRecordingState.Recording };
        VoiceRecording.StartTimer();
        IsVoiceRecording = true;

        _ = AutoStopAfterLimitAsync();
    }

    private async Task AutoStopAfterLimitAsync()
    {
        _autoStopCts?.CancelAsync();
        _autoStopCts?.Dispose();
        _autoStopCts = new CancellationTokenSource();
        var ct = _autoStopCts.Token;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(MaxDuration), ct);
            if (IsVoiceRecording && IsAlive)
                await StopAndSend();
        }
        catch (OperationCanceledException) { /* Отмена нормальная, не считаем ошибкой */ }
    }

    [RelayCommand]
    private async Task StopAndSend()
    {
        if (!IsVoiceRecording) return;

        VoiceError = null;
        VoiceRecording?.StopTimer();

        _autoStopCts?.CancelAsync();
        _autoStopCts?.Dispose();
        _autoStopCts = null;

        var result = await _audioRecorder.StopAsync();
        IsVoiceRecording = false;

        if (result == null)
        {
            VoiceError = "Ошибка при остановке записи";
            ResetState();
            return;
        }

        if (result.Duration.TotalSeconds < MinDuration)
        {
            VoiceError = "Слишком короткое сообщение";
            result.Dispose();
            ResetState();
            return;
        }

        await SendVoiceMessageAsync(result);
    }

    [RelayCommand]
    private async Task CancelRecording()
    {
        if (!IsVoiceRecording && !IsVoiceSending) return;

        _autoStopCts?.CancelAsync();
        _autoStopCts?.Dispose();
        _autoStopCts = null;

        if (_audioRecorder.IsRecording)
        {
            VoiceRecording?.StopTimer();
            await _audioRecorder.CancelAsync();
        }

        _voiceSendCts?.CancelAsync();
        ResetState();
    }

    private async Task SendVoiceMessageAsync(AudioRecordingResult recording)
    {
        IsVoiceSending = true;
        VoiceRecording?.State = AudioRecordingState.Sending;

        _voiceSendCts?.CancelAsync();
        _voiceSendCts?.Dispose();
        _voiceSendCts = new CancellationTokenSource();
        var ct = _voiceSendCts.Token;

        try
        {
            recording.AudioStream.Position = 0;

            var uploadResult = await Ctx.Api.UploadFileAsync<MessageFileDto>(
                ApiEndpoints.Files.Upload(Ctx.ChatId),
                recording.AudioStream, recording.FileName,
                recording.ContentType, ct);

            if (ct.IsCancellationRequested) return;

            if (!uploadResult.Success || uploadResult.Data == null)
            {
                VoiceError = $"Ошибка загрузки: {uploadResult.Error}";
                return;
            }

            var msg = new MessageDto
            {
                ChatId = Ctx.ChatId,
                Content = null,
                SenderId = Ctx.CurrentUserId,
                IsVoiceMessage = true,
                VoiceDurationSeconds = recording.Duration.TotalSeconds,
                VoiceFileUrl = uploadResult.Data.Url,
                VoiceFileName = uploadResult.Data.FileName,
                VoiceContentType = uploadResult.Data.ContentType,
                VoiceFileSize = uploadResult.Data.FileSize
            };

            var sendResult = await Ctx.Api.PostAsync<MessageDto, MessageDto>(ApiEndpoints.Messages.Create, msg, ct);

            if (sendResult.Success) cancelReply();
            else VoiceError = $"Ошибка отправки: {sendResult.Error}";
        }
        catch (OperationCanceledException) { /* Отмена пользователем */ }
        catch (Exception ex) { VoiceError = $"Ошибка: {ex.Message}"; }
        finally
        {
            recording.Dispose();
            ResetState();
        }
    }

    private void ResetState()
    {
        IsVoiceRecording = false;
        IsVoiceSending = false;
        VoiceRecording?.Dispose();
        VoiceRecording = null;
    }

    public override void Dispose()
    {
        _autoStopCts?.Cancel();
        _autoStopCts?.Dispose();
        _voiceSendCts?.Cancel();
        _voiceSendCts?.Dispose();
        _voiceRecording?.Dispose();
        base.Dispose();
    }
}
