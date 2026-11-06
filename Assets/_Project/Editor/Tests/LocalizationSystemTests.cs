using NUnit.Framework;
using SimpleLocalization;
using UnityEngine;

namespace ThailandHorrorJam.Editor.Tests
{
    [TestFixture]
    public sealed class LocalizationSystemTests
    {
        private const string SampleTsv = @"key	English	Thai
# Comment line should be ignored
ui_play	PLAY	เริ่มเกม
ui_settings	SETTINGS	ตั้งค่า
msg_loop_counter	Loop #{0}	รอบที่ {0}
hint_interact	Press {0} to interact	กด {0} เพื่อมีปฏิสัมพันธ์
only_english	Only English	
";

        [SetUp]
        public void SetUp()
        {
            LocalizationManager.LoadFromRawText(SampleTsv);
            LocalizationManager.SetLanguage(SystemLanguage.English);
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationManager.Reload();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            LocalizationManager.Reload();
        }

        [Test]
        public void ParseTSV_ValidString_CorrectlyPopulatesLanguages()
        {
            Assert.That(LocalizationManager.SupportedLanguages, Contains.Item(SystemLanguage.English));
            Assert.That(LocalizationManager.SupportedLanguages, Contains.Item(SystemLanguage.Thai));
            Assert.AreEqual("PLAY", LocalizationManager.Get("ui_play"));

            LocalizationManager.SetLanguage(SystemLanguage.Thai);
            Assert.AreEqual("เริ่มเกม", LocalizationManager.Get("ui_play"));
        }

        [Test]
        public void Get_MissingTranslation_ReturnsEnglishFallback()
        {
            LocalizationManager.SetLanguage(SystemLanguage.Thai);
            // 'only_english' has an empty cell in Thai column, so it must fallback to English
            string result = LocalizationManager.Get("only_english");
            Assert.AreEqual("Only English", result);
        }

        [Test]
        public void Get_MissingKey_ReturnsMissingKeyWarningFormat()
        {
            string result = LocalizationManager.Get("non_existent_key");
            Assert.AreEqual("MISSING:[non_existent_key]", result);
        }

        [Test]
        public void Get_WithArguments_CorrectlyFormatsString()
        {
            LocalizationManager.SetLanguage(SystemLanguage.English);
            string enFormatted = "msg_loop_counter".Localize(3);
            Assert.AreEqual("Loop #3", enFormatted);

            LocalizationManager.SetLanguage(SystemLanguage.Thai);
            string thFormatted = "msg_loop_counter".Localize(3);
            Assert.AreEqual("รอบที่ 3", thFormatted);
        }

        [Test]
        public void SetLanguage_TriggersOnLanguageChangedEvent()
        {
            bool eventFired = false;
            LocalizationManager.OnLanguageChanged += HandleLanguageChanged;

            void HandleLanguageChanged() => eventFired = true;

            try
            {
                LocalizationManager.SetLanguage(SystemLanguage.Thai);
                Assert.IsTrue(eventFired, "OnLanguageChanged event was not fired.");
                Assert.AreEqual(SystemLanguage.Thai, LocalizationManager.CurrentLanguage);
            }
            finally
            {
                LocalizationManager.OnLanguageChanged -= HandleLanguageChanged;
            }
        }

        [Test]
        public void CommentLines_StartingWithHash_AreIgnored()
        {
            Assert.IsFalse(LocalizationManager.HasKey("# Comment line should be ignored"));
        }

        [Test]
        public void ParseCSV_WithQuotesAndCommas_CorrectlyParses()
        {
            string sampleCsv = "key,English,Thai\n" +
                "hint_throw,\"Right Click to Aim, Left Click to Throw\",คลิกขวาเพื่อเล็ง คลิกซ้ายเพื่อปักเข็ม\n" +
                "dialogue_quote,\"He said, \"\"Run!\"\"\",\"เขากล่าวว่า, \"\"วิ่ง!\"\"\"\n";

            LocalizationManager.LoadFromRawText(sampleCsv);
            LocalizationManager.SetLanguage(SystemLanguage.English);
            Assert.AreEqual("Right Click to Aim, Left Click to Throw", LocalizationManager.Get("hint_throw"));
            Assert.AreEqual("He said, \"Run!\"", LocalizationManager.Get("dialogue_quote"));

            LocalizationManager.SetLanguage(SystemLanguage.Thai);
            Assert.AreEqual("เขากล่าวว่า, \"วิ่ง!\"", LocalizationManager.Get("dialogue_quote"));
        }

        [Test]
        public void ConfiguredFont_IsPrimaryFont_2005_iannnnnAMD()
        {
            var config = Resources.Load<LocalizationConfig>(LocalizationManager.CONFIG_RESOURCE_PATH);
            Assert.IsNotNull(config, "LocalizationConfig should exist in Resources.");
            Assert.IsNotNull(config.DefaultFont, "DefaultFont in LocalizationConfig must be assigned.");
            Assert.AreEqual("2005_iannnnnAMD SDF", config.DefaultFont.name);

            LocalizationManager.SetConfig(config);
            var thaiFont = LocalizationManager.GetFont(SystemLanguage.Thai);
            var englishFont = LocalizationManager.GetFont(SystemLanguage.English);

            Assert.IsNotNull(thaiFont, "Thai font must not be null.");
            Assert.AreEqual("2005_iannnnnAMD SDF", thaiFont.name);
            Assert.IsNotNull(englishFont, "English font must not be null.");
            Assert.AreEqual("2005_iannnnnAMD SDF", englishFont.name);
        }

        [Test]
        public void LanguageToggleUI_CyclesBetweenEnglishAndThai()
        {
            var go = new GameObject("TestToggleUI");
            try
            {
                var btn = go.AddComponent<UnityEngine.UI.Button>();
                var toggle = go.AddComponent<LanguageToggleUI>();

                LocalizationManager.SetLanguage(SystemLanguage.English);
                Assert.AreEqual(SystemLanguage.English, LocalizationManager.CurrentLanguage);

                toggle.OnToggleClicked();
                Assert.AreEqual(SystemLanguage.Thai, LocalizationManager.CurrentLanguage);

                toggle.OnToggleClicked();
                Assert.AreEqual(SystemLanguage.English, LocalizationManager.CurrentLanguage);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
