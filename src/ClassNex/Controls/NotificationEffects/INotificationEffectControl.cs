namespace ClassNex.Controls.NotificationEffects;


public interface INotificationEffectControl
{
    
    void Play();

    
    event EventHandler? EffectCompleted;
}
