import json, re, subprocess
from pathlib import Path

base = '9ead5a5396cfb7a856a4e07d279ea267f1cf7cad'
renamed_keys = {
    "onboardingLessons.build.add.alternativeEI": "onboarding.greeting.intervalHelp",
    "onboardingWindow.refreshReadiness.reason.alternativeD": "onboarding.greeting.invalidInterval",
    "settingsPage.saveGreetingInterval.showNotice": "settingsPage.greeting.invalidInterval",
    "settingsPage.settingsPage.text.alternativeP": "settingsPage.greeting.interval",
    "settingsPage.settingsPage.text.alternativeS": "settingsPage.greeting.intervalHelp",
    "aiConfigurationPanel.aiConfigurationPanel.headerSettingsPageHeader.alternativeB": "aiConfiguration.conversationModelTab",
    "conversationComposer.buildSlashItems.hint.alternativeD": "conversation.modelPicker.hint",
    "conversationComposer.refreshModels.automationAutomationPropertiesSetName": "conversation.modelPicker.accessibleName",
    "onboardingLessons.build.add.alternativeBO": "onboarding.speechRecognition.help",
    "onboardingLessons.build.add.alternativeEG": "onboarding.greeting.aiHelp",
    "onboardingLessons.build.add.alternativeIM": "onboarding.externalTools.credentialsHelp",
    "onboardingLessons.chapters.chapters.alternativeH": "onboarding.conversationModel.chapter",
    "onboardingWindow.testText.status.alternativeA": "onboarding.conversationModel.testNotConfigured",
    "settingsPage.settingsPage.text.alternativeN": "settingsPage.greeting.aiHelp",
    "settingsPage.settingsPage.text.alternativeO": "settingsPage.greeting.quietHelp",
    "settingsPage.settingsPage.toggle.alternativeG": "settingsPage.greeting.enabled",
    "settingsPage.settingsPage.toggle.alternativeH": "settingsPage.greeting.aiEnabled",
    "usageStatisticsPage.usageStatisticsPage.usageVisuals": "usageStatistics.conversationModelsHelp",
    "imageDescription.title": "imageDescription.settings.title",
    "imageDescription.help": "imageDescription.settings.help",
    "desktopObservation.title": "desktopObservation.settings.title",
    "desktopObservation.enabled": "desktopObservation.settings.enabled",
    "desktopObservation.help": "desktopObservation.settings.help",
    "desktopObservation.preview": "desktopObservation.preview.action",
    "desktopObservation.uploadTest": "desktopObservation.test.action",
    "desktopObservation.excludedApps": "desktopObservation.exclusions.title",
    "desktopObservation.noDestination": "desktopObservation.settings.noDestination",
    "desktopObservation.routeDirect": "desktopObservation.route.direct",
    "desktopObservation.routeDescribed": "desktopObservation.route.described",
    "desktopObservation.routeConfigure": "desktopObservation.route.configure",
    "desktopObservation.countdown": "desktopObservation.preview.countdown",
    "desktopObservation.previewEmpty": "desktopObservation.preview.empty",
    "desktopObservation.consentBody": "desktopObservation.consent.description",
    "personaProfiles.defaults.ebao": "personaProfiles.defaultsUpdated.ebao",
    "personaProfiles.defaults.dafeiyu": "personaProfiles.defaultsUpdated.dafeiyu",
    "personaProfiles.defaults.marfu": "personaProfiles.defaultsUpdated.marfu",
    "personaProfiles.defaults.xiaobei": "personaProfiles.defaultsUpdated.xiaobei",
    "personaProfiles.defaults.gugugaga": "personaProfiles.defaultsUpdated.gugugaga",
    "personaProfiles.defaults.agent": "personaProfiles.defaultsUpdated.agent",
    "personaProfiles.defaults.generic": "personaProfiles.defaultsUpdated.generic"
}
new_keys = {'desktopObservation.'+key for key in ['captureMode','application','fullScreen','exclusionsHelp','saveExclusions','details','emptyExclusions']}
new_keys |= {'settingsPage.greetingPreviewHelp','settingsPage.greetingJitter'}
new_keys |= {'personaProfiles.screenAccess'} | {'personaProfiles.legacyScreenAccess.'+key for key in ['ebao','dafeiyu','marfu','generic']}
new_keys |= {'sidebar.deleteSection.title','sidebar.deleteSection.description','sidebar.deleteSection.default'}
new_keys |= set(renamed_keys.values())
new_keys |= {'fileActionsMenu.chooseApplication', 'sideChatPane.contextHint'}
formatted = {'desktopObservation.'+key for key in ['countdown', 'consentBody', 'imageDestination', 'textDestination', 'lastObserved']}
formatted |= {renamed_keys[key] for key in formatted if key in renamed_keys}
sentinels = re.compile(r'PLACE(?:HOLDER|WAY)ZERO|PLATZHALTERZERO|प्लेसहोल्डरशून्य|プレースホルダーゼロ', re.I)
keys = None
for path in Path('assets/locales').glob('*.json'):
    old = json.loads(subprocess.check_output(['git', '-c', 'safe.directory=' + Path.cwd().as_posix(), 'show', base + ':' + path.as_posix()], encoding='utf-8'))
    current = json.loads(path.read_bytes())
    added = {k: v for k, v in current.items() if k.startswith('desktopObservation.')}
    assert all(current[k] == v for k, v in old.items()), path
    assert len(added) == 76 + sum(key.startswith('desktopObservation.') for key in renamed_keys.values()), path
    assert set(current) - set(old) == new_keys, path
    for key, value in added.items():
        assert re.fullmatch(r'[a-z][A-Za-z0-9]*(\.[a-z][A-Za-z0-9]*)*', key), (path, key)
        assert value.strip() and not sentinels.search(value), (path, key, value)
        assert value.count('{0}') == (1 if key in formatted else 0), (path, key, value)
        assert not re.search(r'\{[1-9][0-9]*\}', value), (path, key, value)
    image, text = 'https://image.example/path', 'https://chat.example/path'
    summary = added['desktopObservation.imageDestination'].format(image) + '\n' + added['desktopObservation.textDestination'].format(text)
    body = added['desktopObservation.consent.description'].format(summary)
    assert body.count(image) == body.count(text) == 1 and not sentinels.search(body), (path, body)
    assert added['desktopObservation.resume'] != 'CV', path
    if keys is None: keys = set(added)
    assert set(added) == keys, path
    print(path.name, 'all historical values protected, ' + str(len(added)) + ' observation keys, grammar, placeholders and formatted recipients passed')
