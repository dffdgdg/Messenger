using MessengerDesktop.Services.Audio;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MessengerDesktop.ViewModels.Chat;

public partial class ChatViewModel
{
    private IAudioRecorderService _audioRecorder = null!;
    private TranscriptionPoller _transcriptionPoller = null!;

    private VoiceRecordingViewModel? _voiceRecording;
    private CancellationTokenSource? _voiceSendCts;
    private CancellationTokenSource? _autoStopCts;

    private const double MinVoiceDurationSeconds = 0.5;
    private const double MaxVoiceDurationSeconds = 300;

    [ObservableProperty] private bool _isVoiceRecording;
    [ObservableProperty] private bool _isVoiceSending;
    [ObservableProperty] private string _voiceElapsed = "0:00";
    [ObservableProperty] private string? _voiceError;
    [ObservableProperty] private bool _isVoiceSupported;

    public VoiceRecordingViewModel? VoiceRecording
    {
        get => _voiceRecording;
        private set => SetProperty(ref _voiceRecording, value);
    }

    private void InitializeVoice(IAudioRecorderService audioRecorder)
    {
        _audioRecorder = audioRecorder;
        _transcriptionPoller = new TranscriptionPoller(_apiClient);
        IsVoiceSupported = _audioRecorder.IsSupported;
    }

    private async Task DisposeVoiceAsync()
    {
        if (_autoStopCts is not null)
        {
            await _autoStopCts.CancelAsync();
            _autoStopCts.Dispose();
            _autoStopCts = null;
        }

        _voiceRecording?.Dispose();
        _voiceRecording = null;

        _transcriptionPoller?.Dispose();

        if (_voiceSendCts is not null)
        {
            await _voiceSendCts.CancelAsync();
            _voiceSendCts.Dispose();
            _voiceSendCts = null;
        }
    }

    [RelayCommand]
    private async Task StartVoiceRecording()
    {
        if (IsVoiceRecording || IsVoiceSending)
            return;

        if (!_audioRecorder.IsSupported)
        {
            VoiceError =
                "Запись аудио не поддерживается на этой платформе";
            return;
        }

        VoiceError = null;
        var started = await _audioRecorder.StartAsync();

        if (!started)
        {
            VoiceError =
                "Не удалось начать запись. Проверьте микрофон.";
            return;
        }

        _voiceRecording?.Dispose();
        VoiceRecording = new VoiceRecordingViewModel(_audioRecorder);
        VoiceRecording.State = AudioRecordingState.Recording;
        VoiceRecording.StartTimer();

        IsVoiceRecording = true;

        _ = AutoStopAfterLimitAsync();
    }

    private async Task AutoStopAfterLimitAsync()
    {
        if (_autoStopCts is not null)
        {
            await _autoStopCts.CancelAsync();
            _autoStopCts.Dispose();
        }

        _autoStopCts = new CancellationTokenSource();
        var ct = _autoStopCts.Token;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(MaxVoiceDurationSeconds), ct);

            if (IsVoiceRecording && !_disposed)
                await StopAndSendVoice();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Voice] AutoStop error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task StopAndSendVoice()
    {
        if (!IsVoiceRecording)
            return;

        VoiceError = null;
        VoiceRecording?.StopTimer();

        if (_autoStopCts is not null)
        {
            await _autoStopCts.CancelAsync();
            _autoStopCts.Dispose();
            _autoStopCts = null;
        }

        var result = await _audioRecorder.StopAsync();
        IsVoiceRecording = false;

        if (result == null)
        {
            VoiceError = "Ошибка при остановке записи";
            ResetVoiceState();
            return;
        }

        if (result.Duration.TotalSeconds < MinVoiceDurationSeconds)
        {
            VoiceError = "Слишком короткое сообщение";
            result.Dispose();
            ResetVoiceState();
            return;
        }

        await SendVoiceMessageAsync(result);
    }

    [RelayCommand]
    private async Task CancelVoiceRecording()
    {
        if (!IsVoiceRecording && !IsVoiceSending)
            return;

        if (_autoStopCts is not null)
        {
            await _autoStopCts.CancelAsync();
            _autoStopCts.Dispose();
            _autoStopCts = null;
        }

        if (_audioRecorder.IsRecording)
        {
            VoiceRecording?.StopTimer();
            await _audioRecorder.CancelAsync();
        }

        if (_voiceSendCts is not null)
            await _voiceSendCts.CancelAsync();

        ResetVoiceState();
    }

    private async Task SendVoiceMessageAsync(AudioRecordingResult recording)
    {
        IsVoiceSending = true;
        if (VoiceRecording != null)
            VoiceRecording.State = AudioRecordingState.Sending;

        if (_voiceSendCts is not null)
        {
            await _voiceSendCts.CancelAsync();
            _voiceSendCts.Dispose();
        }

        _voiceSendCts = new CancellationTokenSource();
        var ct = _voiceSendCts.Token;

        try
        {
            recording.AudioStream.Position = 0;

            // Загружаем аудиофайл на сервер
            var uploadResult = await _apiClient.UploadFileAsync<MessageFileDto>(
                ApiEndpoints.Files.Upload(_chatId),
                recording.AudioStream,
                recording.FileName,
                recording.ContentType,
                ct);

            if (ct.IsCancellationRequested) return;

            if (!uploadResult.Success || uploadResult.Data == null)
            {
                VoiceError = $"Ошибка загрузки: {uploadResult.Error}";
                return;
            }

            var uploadedFile = uploadResult.Data;

            // Создаём сообщение с голосовыми метаданными
            var msg = new MessageDto
            {
                ChatId = _chatId,
                Content = null,
                SenderId = UserId,
                IsVoiceMessage = true,
                VoiceDurationSeconds = recording.Duration.TotalSeconds,
                VoiceFileUrl = uploadedFile.Url,
                VoiceFileName = uploadedFile.FileName,
                VoiceContentType = uploadedFile.ContentType,
                VoiceFileSize = uploadedFile.FileSize,
                ReplyToMessageId = ReplyingToMessage?.Id
            };

            var sendResult = await _apiClient.PostAsync<MessageDto, MessageDto>(
                ApiEndpoints.Messages.Create, msg, ct);

            if (ct.IsCancellationRequested) return;

            if (sendResult.Success)
                CancelReply();
            else
                VoiceError = $"Ошибка отправки: {sendResult.Error}";
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[Voice] Send cancelled");
        }
        catch (Exception ex)
        {
            VoiceError = $"Ошибка: {ex.Message}";
        }
        finally
        {
            recording.Dispose();
            ResetVoiceState();
        }
    }

    private void ResetVoiceState()
    {
        IsVoiceRecording = false;
        IsVoiceSending = false;

        VoiceRecording?.Dispose();
        VoiceRecording = null;
    }

    public void StartTranscriptionPollingIfNeeded(MessageViewModel message)
    {
        if (!message.IsVoiceMessage) return;
        if (message.TranscriptionStatus is TranscriptionStatus.Done or null) return;

        _transcriptionPoller.StartPolling(message.Id, result =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_disposed) return;
                message.UpdateTranscription(result.Status, result.Transcription);
            });
        });
    }

    [RelayCommand]
    private async Task RetryTranscription(MessageViewModel? message)
    {
        if (message?.IsVoiceMessage != true) return;

        message.UpdateTranscription(TranscriptionStatus.Pending, null);

        var result = await _apiClient.PostAsync(ApiEndpoints.Messages.TranscriptionRetry(message.Id), null);

        if (result.Success)
            StartTranscriptionPollingIfNeeded(message);
        else
            message.UpdateTranscription(TranscriptionStatus.Failed, null);
    }
}