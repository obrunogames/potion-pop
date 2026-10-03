// Potion Pop! — iOS haptics bridge for PotionPop.Haptics (C#: [DllImport("__Internal")] _SP_Haptic(int)).
// type: 0 Light, 1 Medium, 2 Heavy (UIImpactFeedbackGenerator), 3 Success, 4 Warning (UINotificationFeedbackGenerator),
// 5 Selection (UISelectionFeedbackGenerator). Generators are cached and re-prepared to keep latency low.
#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *sp_impact[3];
static UINotificationFeedbackGenerator *sp_notification;
static UISelectionFeedbackGenerator *sp_selection;

static void SPHapticPlay(int type)
{
    switch (type)
    {
        case 0:
        case 1:
        case 2:
        {
            if (sp_impact[type] == nil)
            {
                UIImpactFeedbackStyle style = type == 0 ? UIImpactFeedbackStyleLight
                                            : type == 1 ? UIImpactFeedbackStyleMedium
                                                        : UIImpactFeedbackStyleHeavy;
                sp_impact[type] = [[UIImpactFeedbackGenerator alloc] initWithStyle:style];
            }
            [sp_impact[type] impactOccurred];
            [sp_impact[type] prepare];
            break;
        }
        case 3:
        case 4:
        {
            if (sp_notification == nil) sp_notification = [[UINotificationFeedbackGenerator alloc] init];
            [sp_notification notificationOccurred:(type == 3 ? UINotificationFeedbackTypeSuccess
                                                             : UINotificationFeedbackTypeWarning)];
            [sp_notification prepare];
            break;
        }
        case 5:
        {
            if (sp_selection == nil) sp_selection = [[UISelectionFeedbackGenerator alloc] init];
            [sp_selection selectionChanged];
            [sp_selection prepare];
            break;
        }
        default:
            break;
    }
}

extern "C" void _SP_Haptic(int type)
{
    // UIKit feedback generators must be used on the main thread (Unity normally calls from it already).
    if ([NSThread isMainThread]) SPHapticPlay(type);
    else dispatch_async(dispatch_get_main_queue(), ^{ SPHapticPlay(type); });
}
