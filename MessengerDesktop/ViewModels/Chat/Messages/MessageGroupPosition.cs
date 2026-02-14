namespace MessengerDesktop.ViewModels.Chat;

/// <summary>
/// Позиция сообщения в группе для определения радиусов пузыря
/// </summary>
public enum MessageGroupPosition
{
    /// <summary>Одиночное сообщение</summary>
    Alone,
    /// <summary>Первое в группе</summary>
    First,
    /// <summary>Среднее в группе</summary>
    Middle,
    /// <summary>Последнее в группе</summary>
    Last
}