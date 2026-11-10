using Microsoft.Toolkit.Uwp.Notifications;
using Windows.UI.Notifications;

namespace CrashWatch;

/// <summary>
/// Wraps the CommunityToolkit toast API. Note: the first toast triggers the toolkit to
/// create a Start-menu shortcut carrying our AUMID — required for unpackaged desktop apps.
/// </summary>
internal static class ToastService
{
    public static void Show(string title, string body, bool critical = false)
    {
        try
        {
            var content = new ToastContent
            {
                Scenario = critical ? ToastScenario.Reminder : ToastScenario.Default,
                Visual = new ToastVisual
                {
                    BindingGeneric = new ToastBindingGeneric
                    {
                        Children =
                        {
                            new AdaptiveText { Text = title },
                            new AdaptiveText { Text = body },
                        },
                    },
                },
            };

            if (critical)
            {
                // Reminder scenario requires an action to stay on screen.
                content.Actions = new ToastActionsCustom { Buttons = { new ToastButtonDismiss() } };
            }

            var notifier = ToastNotificationManagerCompat.CreateToastNotifier();
            notifier.Show(new ToastNotification(content.GetXml()));
            Log.Write($"toast shown [{(critical ? "critical" : "normal")}]: {title}");
        }
        catch (Exception ex)
        {
            Log.Write("toast failed: " + ex.Message);
        }
    }
}
