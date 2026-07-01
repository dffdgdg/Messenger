using Core.Features.Chat.ViewModels.Context;
using Core.Features.Chat.ViewModels.Shared;
using Core.Features.Chat.ViewModels.Voice;
using Core.Services.Media.Audio;
using static Core.Shared.Configuration.ApiEndpoints;

namespace Core.Features.Chat.ViewModels.Handlers.Media;

/// <summary>
/// <param name="cancelReply">
/// ReplyHandler нужен для CancelReply после отправки голосового.
/// Передаётся как Func для избежания циклических зависимостей.
/// </param>
/// </summary>
public partial class ChatVoiceHandler(ChatContext context, Action cancelReply) : ChatFeatureHandler(context)
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

    private VoiceRecordingViewModel? _voiceRecording;
    public VoiceRecordingViewModel? VoiceRecording
    {
        get => _voiceRecording;
        private set => SetProperty(ref _voiceRecording, value);
    }

    public void Initialize(IAudioRecorderService audioRecorder)
    {
        _audioRecorder = audioRecorder;
    }

    [RelayCommand]
    private async Task StartRecording()
    {
        if (IsVoiceRecording || IsVoiceSending) return;

        VoiceError = null;
        var started = await _audioRecorder.StartAsync();

        if (!started)
        {
            VoiceError = "Не удалось начать запись. Проверьте микрофон.";
            return;
        }

        _voiceRecording?.Dispose();
        VoiceRecording = new VoiceRecordingViewModel(_audioRecorder)
        {
            State = AudioRecordingState.Recording
        };
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
        catch (OperationCanceledException) { /* Отмена нормальная */ }
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
//TODO Замечание(не блокер, но стоит знать) : точно так же, как и для обычных файлов(api/files/{ file.Id}/download проверяет доступ к исходному file.Message.ChatId), для пересланного голосового voice.MessageId — это id оригинального сообщения-владельца файла, а не id пересланного сообщения в текущем чате. Если получатель пересылки не состоит в исходном чате, скачивание не пройдёт EnsureMemberOfAsync. Это существующее архитектурное ограничение, общее с файлами — я его не трогаю в рамках этой задачи, но имей в виду, если будете фиксить пересылку файлов отдельно.
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
                recording.AudioStream,
                recording.FileName,
                recording.ContentType,
                ct);

            if (ct.IsCancellationRequested) return;

            if (!uploadResult.Success || uploadResult.Data?.UploadToken is null)
            {
                VoiceError = $"Ошибка загрузки: {uploadResult.Error}";
                return;
            }

            var request = new CreateMessageRequest
            {
                ChatId = Ctx.ChatId,
                IsVoiceMessage = true,
                VoiceUploadToken = uploadResult.Data.UploadToken,
                VoiceDurationSeconds = recording.Duration.TotalSeconds,
                VoiceWaveform = recording.Waveform
            };

            var sendResult = await Ctx.Api.PostAsync<CreateMessageRequest, MessageDto>(ApiEndpoints.Messages.Create, request, ct);

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

    protected override void DisposeManagedResources()
    {
        _autoStopCts?.Cancel();
        _autoStopCts?.Dispose();
        _autoStopCts = null;

        _voiceSendCts?.Cancel();
        _voiceSendCts?.Dispose();
        _voiceSendCts = null;

        _voiceRecording?.Dispose();
        _voiceRecording = null;
    }
}