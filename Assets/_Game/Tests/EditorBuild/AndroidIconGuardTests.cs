#if UNITY_ANDROID
using NUnit.Framework;
using PotionPop.EditorTools;
using UnityEditor;
using UnityEditor.Build;

namespace PotionPop.Tests
{
    public sealed class AndroidIconGuardTests
    {
        [Test]
        public void EmptyAdaptiveForegroundPreventsBuild()
        {
            var kind = UnityEditor.Android.AndroidPlatformIconKind.Adaptive;
            var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            Assert.That(slots.Length, Is.GreaterThan(0));
            var original = slots[0].GetTexture(1);
            try
            {
                slots[0].SetTexture(null, 1);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
                Assert.Throws<BuildFailedException>(PotionPopBuilder.ValidateAndroidIcons);
            }
            finally
            {
                slots[0].SetTexture(original, 1);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
            }
            Assert.DoesNotThrow(PotionPopBuilder.ValidateAndroidIcons);
        }

        [Test]
        public void WrongAdaptiveBackgroundPreventsBuild()
        {
            var kind = UnityEditor.Android.AndroidPlatformIconKind.Adaptive;
            var slots = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
            var original = slots[0].GetTexture(0);
            try
            {
                slots[0].SetTexture(AssetDatabase.LoadAssetAtPath<UnityEngine.Texture2D>(PotionPopBuilder.AppIconPath), 0);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
                Assert.Throws<BuildFailedException>(PotionPopBuilder.ValidateAndroidIcons);
            }
            finally
            {
                slots[0].SetTexture(original, 0);
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, slots);
            }
            Assert.DoesNotThrow(PotionPopBuilder.ValidateAndroidIcons);
        }
    }
}
#endif
