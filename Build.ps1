param([ValidateSet('PreferenceSaveTests','CompanionPreparationUiTests','SettingsPerformanceTests','ImChannelUiTests','RequestLanguageSnapshotTests','LiveLanguageUiTests','LiveLanguageTests','UiLanguageChangeTests','BuildCompileOnlyProbe','SettingsFeatureNavigationTests','RelationshipExperienceTests','RelationshipExperienceUiTests','UninstallUiTests','QuietMotionTests','HolidayKeepsakeUiTests','HolidayKeepsakeTests','DataManagementTests','DataManagementUiTests','HistoryMaintenanceTests','DataMaintenanceHostTests','DataMaintenanceAdmissionTests','CacheMaintenanceTests','CacheMaintenanceUiTests','RelationshipFeedbackTests','CompanionHomeUiTests','RelationshipTitleFontTests','RelationshipStageArtTests','RelationshipPresentationTests', 'WpfRelationshipPackTests','RelationshipIntegrationTests','RelationshipRulesTests','RelationshipStoreTests','RelationshipIdentityTests','AgentJobUiTests','AgentJobViewStateTests','AgentJobProjectTests','UiControlSnapshotTests','AgentJobLifecycleUiTests','AgentJobLifecycleTests','SpeechLanguagePolicyTests','LanguagePickerTests','SystemLanguageTests','ReplyLanguagePolicyTests','MultilingualUiAuditTests','LanguagePackTests','AgentJobSessionTests','AgentJobIntegrationTests','AgentJobToolTests','AgentJobSandboxTests','AgentJobProcessTests','AgentJobManagerTests','AgentJobLogTests','AgentJobStoreTests','AgentJobContractTests','CompanionStartupTests','LocalizationMissingResourceTests','LocalizationTests','LocalizationUiTests','PowerShellStartupTests','OfficialCompanionLiveSmoke','WpfOfficialCompanionTests','OfficialCompanionPackageSmoke','OfficialCompanionLegacyTests','OfficialCompanionIdentityTests','HolidaySourceBindingTests','OfficialCompanionTests','ConversationArchiveRegressionTests','HolidayAssetTests','HolidayProductionSmoke','HolidayRuntimeTests','HolidayHoverTests','HolidayIdentityTests','HolidayVisualSmoke','HolidaySettingsSmoke','HolidayGreetingTests','HolidayStateTests','CompleteCompanionNativeTests','LocalImportUiTests','ShortcutCaptureUiTests','ShortcutCaptureTests','ComposerDraftStoreTests','SeasonalKeepsakeUiTests','SeasonalKeepsakeTests','CompleteCompanionClockTests','SeasonalIntegrationTests','CompleteCompanionTests','HolidayCalendarTests','ImageTaskCenterLifecycleTests','ImageTaskCenterUiTests','ImageGalleryExportTests','ImageGalleryManagementTests','GeneratedImageGroupTests','ImageGenerationMotionTests','ImageGenerationTaskManagerTests','ImageGenerationTaskStoreTests', 'LanServiceTests','LanSettingsUiTests','SkillRepositoryTests','SkillDiscoveryUiTests','AgentQuestionUiSmoke','ImageGenerationUiTests','ImageWorkflowLiveTests','ImageGenerationLiveTests','ImageGenerationIntegrationTests','ImageGenerationBackendTests','ImageGalleryLayoutTests','ImageProviderProtocolTests','AgentProjectToolsTests','ContextPreviewTests','ThemeTests','LiveWaveformTests','ReleaseIdentityTests','UpdateChannelTests','ReleaseVersionTests','App','BrowserProfileCleanupTests','ManualRunTests','GuideBookendTests','PopupLifecycleTests','ChatImagePreviewTests','ManagedSkillLibraryTests','PluginsUiTests','TabLayoutControlsTests','ShortcutHelpTests','AppShellTests','EmbeddedSettingsTests','UsageTrackingTests','UsageStatisticsUiTests','TrayInteractionTests','ChangeSummaryPersistenceTests','ChangeSummaryIntegrationTests','ConversationViewportTests','UnifiedWorkspaceTests','UnifiedPreviewTabsTests','SummaryNavigationTests','SideChatTests','BrowserPaneTests','SidebarWorkspaceTests','TerminalUiTests','WindowsTerminalTests','ProcessAvatarTests','ChatAtmosphereTests','FilePreviewTests','FinalOutputFilesTests','ConversationSwitchRegressionTests','PermissionUiSmoke','WorkspacePermissionTests','WorkspaceSandboxTests','AgentEvolutionRegressionTests','AgentTaskRunnerTests','McpUiSmoke','AgentIsolationTests','AgentCodingWorkflowTests','AgentTaskMemoryTests','McpConnectionTests','AgentRunRecoveryTests','AgentSubagentExecutionTests','AgentInstructionTests','AgentContextPolicyTests','AgentTerminalTests','AgentApprovalUiSmoke','AgentApprovalTests','AgentCapabilityTests','PetRuntimeTests','GuidanceUiTests','ComposerRenderingTests','DeleteDialogTests','UiResourceTests','QueueHandleTests','ConversationVisualTests','ConversationOwnershipUiTests','WelcomeSuggestionTests','DetailedGuideTests','ConversationModeUiTests','ConversationOwnershipTests','SidebarActionTests','SidebarProjectInteractionTests','SettingsMaximizeTests','ModelConfigurationSmoke','ContextUsageTests','DevReviewRegressionTests','SidebarSectionsTests','DraftConversationTests','ComposerPaletteTests','CoreTests','ApiTests','ProfileTests','ConversationTests','ChatSmoke','AudioSmoke','UiSmoke','MotionSmoke','LiveTests','UpdateTests','StartupTests','ReleaseUiSmoke','ProxyTests','GreetingTests','UpdateApplyTests','MenuSmoke','AutoUpdateSmoke','HistoryTests','StreamingTests','QuietTests','FeatureUiSmoke','VoicePresetTests','InteractionTests','SpriteAlignmentTests','InteractionUiSmoke','ActionPaletteSmoke','SettingsNavigationSmoke','OnboardingSmoke','OnboardingModelSmoke','LiveCallSmoke','GuideMenuSmoke','QueueTests','ComposerSmoke','DictationTests','AboutSmoke','SteeringSmoke','FeedbackSmoke','ProviderTests','ProviderUiSmoke','ImportedSkinTests','XiaobeiUiSmoke','AiGreetingTests','AiGreetingUiSmoke','BundledSkinTests','SkinPackExport','StumbleSmoke','HoverSleepSmoke','OnboardingUpgradeTests','RecorderFailureTests','ConversationUiSmoke','MarkdownTests','AgentWorkspaceTests','AgentProtocolTests','AgentSessionTests','ApplicationSnapshotTests','AttachmentPickerTests','WorkSettingsTests','UpdateScheduleTests','AgentReliabilityTests','AgentFileToolsTests','AgentChangesTests','AgentDocumentTests','AgentCommandTests','AgentTaskTests','AgentWorkbenchSmoke','AgentContextTests','PredictionTests','AgentDiffTests','AgentChangePreviewTests','ProjectCatalogTests','ConversationWorkspaceTests','ConversationWorkspaceManagerTests','ProjectSidebarSmoke','GitHubToolsTests','AgentSessionModelTests','AgentKernelTests','AgentKernelIntegrationTests','AgentTaskBindingTests','AgentContextRecoveryTests','AgentProfileTests','AgentProviderKernelTests','AgentSubagentToolTests','AgentIntegrationFinalTests','AgentKernelCompletenessTests','ConversationSingleWindowSmoke','ConversationRepeatSmoke','ConversationTitleTests')][string]$Target = 'App', [string]$OutputDirectory = '', [string[]]$TestArguments = @())
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '.NET Framework compiler is required.' }
$speechAssembly = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech') -Filter 'System.Speech.dll' -Recurse | Select-Object -First 1 -ExpandProperty FullName
if (!$speechAssembly) { throw 'Windows System.Speech assembly was not found.' }
$referenceFiles = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Net.Http.dll','System.Runtime.Serialization.dll','System.Security.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$references = @($referenceFiles | ForEach-Object { '/reference:' + (Join-Path $frameworkRoot $_) }) + @('/reference:' + $speechAssembly)
if ($Target -eq 'AttachmentPickerTests') { foreach ($automation in @('UIAutomationClient.dll','UIAutomationTypes.dll')) { $references += '/reference:' + (Join-Path $frameworkRoot ('WPF\' + $automation)) } }
$workRoot = Join-Path $projectRoot 'work'
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
if ($Target -eq 'App') {
    $outputRoot = Join-Path (Split-Path $projectRoot -Parent) 'outputs\DafeiyuDeskPet'
    if ($OutputDirectory) { $outputRoot = [IO.Path]::GetFullPath($OutputDirectory) }
    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $destination = Join-Path $outputRoot '桌面伙伴.exe'
    $sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -Recurse | Select-Object -ExpandProperty FullName)
    $options = @('/target:winexe','/main:Dafeiyu.Program',('/win32manifest:' + (Join-Path $projectRoot 'app.manifest')),('/win32icon:' + (Join-Path $projectRoot 'assets\pet.ico')))
} else {
    $destination = Join-Path $workRoot ($Target + '.exe')
    $sourceNames = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' -Recurse | ForEach-Object { $_.FullName.Substring((Join-Path $projectRoot 'src').Length + 1) })
    $sources = @($sourceNames | ForEach-Object { Join-Path $projectRoot ('src\' + $_) } | Where-Object { Test-Path -LiteralPath $_ }) + @((Join-Path $projectRoot ('tests\' + $Target + '.cs')))
    if ($Target -eq 'ModelConfigurationSmoke' -or $Target -eq 'ContextUsageTests' -or $Target -eq 'UiSmoke' -or $Target -eq 'ChatSmoke' -or $Target -eq 'AudioSmoke' -or $Target -eq 'LiveTests' -or $Target -eq 'UpdateTests' -or $Target -eq 'ProxyTests' -or $Target -eq 'ProviderUiSmoke' -or $Target -eq 'OnboardingModelSmoke') { $sources += Join-Path $projectRoot 'tests\ApiTests.cs' }
    if ($Target -eq 'ChatSmoke' -or $Target -eq 'ProviderUiSmoke') { $sources += Join-Path $projectRoot 'tests\LiveTests.cs' }
    if ($Target -eq 'MenuSmoke' -or $Target -eq 'GuideMenuSmoke') {
        & $compiler @('/nologo','/codepage:65001','/target:winexe',('/out:' + (Join-Path $workRoot 'MenuBlankFixture.exe'))) @references (Join-Path $projectRoot 'tests\MenuBlankFixture.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Menu fixture compilation failed.' }
    }
    $options = @('/target:exe',('/main:Dafeiyu.Tests.' + $Target))
    if ($Target -eq 'UpdateApplyTests') {
        # Exercise the actual maintenance helper closure without pulling unrelated
        # desktop code into each synthetic executable copied into the test ZIP.
        $sources = @(Get-Content -LiteralPath (Join-Path $projectRoot 'tools/update-worker-sources.txt') | Where-Object { $_.Trim() } | ForEach-Object { Join-Path $projectRoot ('src/' + $_) })
        $sources += Join-Path $projectRoot 'tests/UpdateApplyTests.cs'
        $options += '/define:UPDATE_WORKER'
    }
    if ($Target -eq 'RelationshipStageArtTests') { $sources += Join-Path $projectRoot 'tests/RelationshipStageArtContractChecks.cs' }
    if ($Target -eq 'QuietMotionTests') { $sources += Join-Path $projectRoot 'tests/QuietKeepsakeChecks.cs' }
    if ($Target -eq 'HolidayKeepsakeUiTests') { $sources += Join-Path $projectRoot 'tests/HolidayKeepsakePresentationChecks.cs' }
    if ($Target -eq 'HolidayKeepsakeTests') { $sources += Join-Path $projectRoot 'tests/HolidayKeepsakeContractChecks.cs' }
    if ($Target -eq 'CompanionHomeUiTests') { $sources += Join-Path $projectRoot 'tests/RelationshipHomeFixtures.cs' }
    if ($Target -like 'Relationship*') { $sources += Join-Path $projectRoot 'tests/RelationshipTestSupport.cs' }
    if ($Target -in @('RelationshipStoreTests','RelationshipIntegrationTests')) { $sources += Join-Path $projectRoot 'tests/RelationshipAtomicWriteChecks.cs' }
    if ($Target -like 'AgentJob*' -or $Target -eq 'DataMaintenanceAdmissionTests') { $sources += Join-Path $projectRoot 'tests/AgentJobTestSupport.cs' }
    if ($Target -in @('AgentJobLifecycleUiTests','AgentJobProjectTests')) { $sources += Join-Path $projectRoot 'tests/AgentJobExitHost.cs' }
    if ($Target -in @('OfficialCompanionTests','WpfOfficialCompanionTests')) { $sources += Join-Path $projectRoot 'tests\OfficialCompanionFixtures.cs' }
    if ($Target -eq 'CompanionStartupTests') { $sources += Join-Path $projectRoot 'tests/OfficialCompanionFixtures.cs'; $sources += Join-Path $projectRoot 'tests/WpfOfficialCompanionTests.cs' }
    if ($Target -eq 'HolidayHoverTests') { $sources += Join-Path $projectRoot 'tests\HolidayRuntimeTests.cs' }
    if ($Target -eq 'LanServiceTests') { $sources += Join-Path $projectRoot 'tests\ApiTests.cs' }
    if ($Target -eq 'SkillDiscoveryUiTests') { $sources += Join-Path $projectRoot 'tests\SkillRepositoryTests.cs' }
    if ($Target -in @('AgentJobUiTests','UiControlSnapshotTests','LanguagePickerTests','MultilingualUiAuditTests','AppShellTests')) { $sources += Join-Path $projectRoot 'tests\UiControlSnapshot.cs' }
    if ($Target -eq 'SettingsMaximizeTests') { $options += '/win32manifest:' + (Join-Path $projectRoot 'app.manifest') }
}
$buildCommit = ""
try { $buildCommit = (@(& git -C $projectRoot rev-parse HEAD 2>$null) -join "").Trim() } catch { $buildCommit = "" }
if ($buildCommit -notmatch '^[a-f0-9]{40}$') { $buildCommit = '' }
$displayVersion = [regex]::Match([IO.File]::ReadAllText((Join-Path $projectRoot 'src/ReleaseInfo.cs')), 'DisplayVersion="([^"]+)"').Groups[1].Value
$identitySource = Join-Path $workRoot ($Target + '.identity.cs')
[IO.File]::WriteAllText($identitySource, ('[assembly:System.Reflection.AssemblyInformationalVersion("'+$displayVersion+$(if($buildCommit){'+'+$buildCommit}else{''})+'")]'),[Text.UTF8Encoding]::new($false))
$sources += $identitySource
$references += '/reference:' + (Join-Path $projectRoot 'vendor/qrcoder/net40/QRCoder.dll')
$assets = @('/resource:' + (Join-Path $projectRoot 'assets\provider-logos.json') + ',Dafeiyu.ProviderLogos')
$assets += '/resource:' + (Join-Path $projectRoot 'vendor/qrcoder/net40/QRCoder.dll') + ',Dafeiyu.QRCoder.dll'
$assets += '/resource:' + (Join-Path $projectRoot 'vendor/qrcoder/LICENSE.txt') + ',Dafeiyu.QRCoder.LICENSE.txt'
$assets += '/resource:' + (Join-Path $projectRoot 'assets/official/companion-package-policy.json') + ',Dafeiyu.CompanionPackagePolicy'
$localeAssets = @('zh_CN','zh_TW','en_US','ja_JP','ko_KR','fr_FR','de_DE','hi_IN','id_ID','it_IT','pt_BR','es_419','es_ES') | ForEach-Object { '/resource:' + (Join-Path $projectRoot ('assets/locales/' + $_ + '.json')) + ',Dafeiyu.Locales.' + $_ + '.json' }
$assets += $localeAssets
# Fixed selected scene list; production and focused fixtures preserve all originals.
foreach ($sceneCharacter in @('e-bao','dafeiyu','marfu','xiaobei','gugugaga')) {
    foreach ($sceneStage in @('first_meeting','familiar','close','trusted','heartfelt','best_friend','halloween','christmas','spring-festival','lantern','valentines-day','qingming','dragon-boat','mid-autumn','national-day','qixi')) {
        $sceneRelative = if ($sceneStage -in @('halloween','christmas','spring-festival','lantern','valentines-day','qingming','dragon-boat','mid-autumn','national-day','qixi')) { 'relationships/holidays/' + $sceneStage + '/' + $sceneCharacter + '.png' } else { 'relationships/' + $sceneCharacter + '/' + $sceneStage + '.png' }
        $scenePath = Join-Path $projectRoot ('assets/' + $sceneRelative)
        if (!(Test-Path -LiteralPath $scenePath)) { throw "Required relationship scene is missing: $scenePath" }
        $assets += '/resource:' + $scenePath + ',Dafeiyu.' + $sceneRelative.Replace('/','.')
    }
}

$assets += '/resource:' + (Join-Path $projectRoot 'assets/holidays/calendar/solar-terms.json') + ',Dafeiyu.Holidays.SolarTerms'
if ($Target -eq 'HolidayCalendarTests') { $assets += '/resource:' + (Join-Path $projectRoot 'tests/fixtures/holidays/calendar-golden.json') + ',Dafeiyu.Tests.HolidayGolden' }
if ($sources | Where-Object { [IO.Path]::GetFileName($_) -eq 'MarkdownContent.cs' }) {
    foreach ($markdownLibrary in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'vendor/markdown') -Filter '*.dll') {
        $references += '/reference:' + $markdownLibrary.FullName
        $assets += '/resource:' + $markdownLibrary.FullName + ',Dafeiyu.Markdown.' + $markdownLibrary.Name
    }
    foreach ($markdownLicense in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'vendor/markdown') -Filter '*LICENSE.txt') {
        $assets += '/resource:' + $markdownLicense.FullName + ',Dafeiyu.Markdown.' + $markdownLicense.Name
    }
}
foreach ($terminalAsset in @('xterm.js','xterm.css','addon-fit.js','LICENSE','fit-LICENSE')) {
    $assets += '/resource:' + (Join-Path $projectRoot ('vendor/xterm/' + $terminalAsset)) + ',Dafeiyu.Terminal.' + $terminalAsset
}
foreach ($webviewAssembly in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll')) {
    $webviewPath = Join-Path $projectRoot ('vendor/webview2/' + $webviewAssembly)
    $references += '/reference:' + $webviewPath
    $assets += '/resource:' + $webviewPath + ',Dafeiyu.WebView2.' + $webviewAssembly
}
foreach ($webviewNative in @('WebView2Loader.x64.dll','WebView2Loader.x86.dll','LICENSE.txt')) {
    $assets += '/resource:' + (Join-Path $projectRoot ('vendor/webview2/' + $webviewNative)) + ',Dafeiyu.WebView2.' + $webviewNative
}
$fullDefaultArtwork = $Target -notin @('App','CompanionStartupTests','WpfOfficialCompanionTests','OfficialCompanionLiveSmoke')
foreach ($quietPet in @('e-bao','dafeiyu','marfu','xiaobei','gugugaga')) {
    if (!$fullDefaultArtwork -and $quietPet -ne 'e-bao') { continue }
    foreach ($quietAction in @('idle','sleeping','sleeping-alternate')) {
        $quietPath=Join-Path $projectRoot ('assets/companion-quiet/'+$quietPet+'/'+$quietAction+'.png')
        if (!(Test-Path -LiteralPath $quietPath)) { throw "Required quiet artwork is missing: $quietPath" }
        $assets += '/resource:'+$quietPath+',Dafeiyu.Quiet.'+$quietPet+'.'+$quietAction
    }
}
foreach ($asset in @(@('spritesheet.png','Dafeiyu.Atlas'),@('pet.ico','Dafeiyu.Icon'),@('pet-preview-hd.png','Dafeiyu.Preview'),@('e-bao-spritesheet.png','Dafeiyu.EbaoAtlas'),@('e-bao-pet-preview-hd.png','Dafeiyu.EbaoPreview'),@('marfu-spritesheet.png','Dafeiyu.MarfuAtlas'),@('marfu-pet-preview-hd.png','Dafeiyu.MarfuPreview'),@('e-bao-interactions.png','Dafeiyu.EbaoInteractions'),@('dafeiyu-interactions.png','Dafeiyu.DafeiyuInteractions'),@('marfu-interactions.png','Dafeiyu.MarfuInteractions'))) {
    if (!$fullDefaultArtwork -and $asset[0] -notin @('pet.ico','e-bao-spritesheet.png','e-bao-pet-preview-hd.png','e-bao-interactions.png')) { continue }
    $assetPath = Join-Path $projectRoot ('assets\' + $asset[0])
    if (!(Test-Path -LiteralPath $assetPath)) { throw "Required artwork is missing: $assetPath" }
    $assets += '/resource:' + $assetPath + ',' + $asset[1]
}
foreach ($avatarId in @('e-bao','dafeiyu','marfu','xiaobei','gugugaga')) {
    $avatarPath = Join-Path $projectRoot ('assets\avatars\' + $avatarId + '.png')
    if (!(Test-Path -LiteralPath $avatarPath)) { throw "Required companion avatar is missing: $avatarPath" }
    $assets += '/resource:' + $avatarPath + ',Dafeiyu.Avatar.' + $avatarId
}
# Reference-matched eight-pose actions are required for all four presets.
foreach ($extraPet in @('e-bao','dafeiyu','marfu','xiaobei')) {
    if (!$fullDefaultArtwork -and $extraPet -ne 'e-bao') { continue }
    $originalExtraPath = Join-Path $projectRoot ('assets\additional\' + $extraPet + '.png')
    if (!(Test-Path -LiteralPath $originalExtraPath)) { throw "Required original artwork is missing: $originalExtraPath" }
    $assets += '/resource:' + $originalExtraPath + ',Dafeiyu.Additional.' + $extraPet
    $extraActions = @('lifted','landing','feeding','happy','sleeping','special','special2','stumble')
    if ($extraPet -eq 'e-bao') { $extraActions += 'jumping' }
    foreach ($extraAction in $extraActions) {
        $extraPath = Join-Path $projectRoot ('assets\companion-animations\' + $extraPet + '\' + $extraAction + '.png')
        if (!(Test-Path -LiteralPath $extraPath)) { throw "Required companion artwork is missing: $extraPath" }
        $assets += '/resource:' + $extraPath + ',Dafeiyu.Companion.' + $extraPet + '.' + $extraAction
    }
}
# Bundled presets are embedded in the app and test executables, keeping the
# portable ZIP compatible with old update workers that reject a pets directory.
$bundleCatalog = Join-Path $projectRoot $(if ($fullDefaultArtwork) { 'assets/bundled-skins/catalog.json' } else { 'assets/official/bundled-catalog.json' })
$assets += '/resource:' + $bundleCatalog + ',Dafeiyu.Bundled.Catalog'
foreach ($bundle in (Get-Content -LiteralPath $bundleCatalog -Raw -Encoding UTF8 | ConvertFrom-Json)) {
$bundledSkinRoot = Join-Path $projectRoot ('assets\bundled-skins\'+$bundle.id)
if (Test-Path -LiteralPath $bundledSkinRoot -PathType Container) {
    $bundledResourceNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($bundledAsset in Get-ChildItem -LiteralPath $bundledSkinRoot -Recurse -File | Sort-Object FullName) {
        $bundledRelative = $bundledAsset.FullName.Substring($bundledSkinRoot.Length + 1)
        if ($bundledRelative -ne 'pet.json' -and $bundledAsset.Extension -ine '.png') { continue }
        $bundledResource = $bundle.resource_prefix + $bundledRelative.Replace('\','.').Replace('/','.')
        if (!$bundledResourceNames.Add($bundledResource)) { throw "Bundled preset resource collision: $bundledRelative" }
        $assets += '/resource:' + $bundledAsset.FullName + ',' + $bundledResource
    }
}
}
# Holiday content uses the same logical paths in Framework and DeskPet.Core.
$holidayRoot = Join-Path $projectRoot 'assets/holidays'
$fullHolidayArtwork = $Target -in @('HolidayAssetTests','HolidayProductionSmoke')
$holidayCatalogFile = if ($fullHolidayArtwork) { 'assets/official/halloween-catalog.json' } else { 'assets/official/holiday-catalog.json' }
$assets += '/resource:' + (Join-Path $projectRoot $holidayCatalogFile) + ',Dafeiyu.Holidays.catalog.json'
foreach ($holidayAsset in Get-ChildItem -LiteralPath $holidayRoot -Recurse -File | Where-Object { $_.Extension -in @('.json','.png') } | Sort-Object FullName) {
    $holidayRelative = $holidayAsset.FullName.Substring($holidayRoot.Length + 1).Replace('\','.').Replace('/','.')
    if ($holidayRelative -eq 'catalog.json') { continue }
    if ($fullHolidayArtwork -and $holidayRelative -notmatch '^halloween\.') { continue }
    if (!$fullHolidayArtwork -and ($holidayRelative -notmatch '^halloween\.(e-bao\.|greetings\.json$)')) { continue }
    $assets += '/resource:' + $holidayAsset.FullName + ',Dafeiyu.Holidays.' + $holidayRelative
}
# UpdateApplyTests copies itself into its synthetic app/helper ZIP; keep that
# fixture small rather than embedding the complete companion artwork twice.
if ($Target -eq 'UpdateApplyTests') { $assets = @($localeAssets) }
if ($Target -in @('LocalizationTests','LanguagePackTests')) { $assets += '/resource:' + (Join-Path $projectRoot 'tests/fixtures/localization/text-baseline.json') + ',Dafeiyu.Tests.LocalizationBaseline'; $assets += '/resource:' + (Join-Path $projectRoot 'tests/fixtures/localization/feature-text-baseline.json') + ',Dafeiyu.Tests.FeatureLocalizationBaseline' }
if ($Target -eq 'LocalizationMissingResourceTests') { $assets = @($assets | Where-Object { $_ -notlike '*,Dafeiyu.Locales.zh_CN.json' }) }
# WPF font Resources must match the actual output assembly, including fixtures.
if ($Target -ne 'UpdateApplyTests') {
    $titleAssemblyName=[IO.Path]::GetFileNameWithoutExtension($destination)
    $titleResources=Join-Path $workRoot ($titleAssemblyName+'.g.resources')
    & (Join-Path $projectRoot 'tools/fonts/Build-WpfTitleResources.ps1') -ProjectRoot $projectRoot -Destination $titleResources
    $assets += '/resource:'+$titleResources+','+$titleAssemblyName+'.g.resources'
}
$arguments = @('/nologo','/optimize+','/codepage:65001','/utf8output','/platform:anycpu',('/out:' + $destination)) + $references + $options + $assets + $sources
$responseFile = Join-Path $workRoot ($Target + '.rsp')
$responseLines = @($arguments | ForEach-Object { '"' + ($_ -replace '(?<!\\)"','\"') + '"' })
[IO.File]::WriteAllLines($responseFile, $responseLines, [Text.UTF8Encoding]::new($true))
& $compiler ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($Target)." }
if ($Target -eq 'App') {
    $workerDestination = Join-Path $outputRoot 'DeskPet.Update.exe'
    $workerSources = @(Get-Content -LiteralPath (Join-Path $projectRoot 'tools/update-worker-sources.txt') | Where-Object { $_.Trim() } | ForEach-Object { Join-Path $projectRoot ('src/' + $_) })
    $workerSources += $identitySource
    & $compiler @('/nologo','/optimize+','/codepage:65001','/utf8output','/platform:anycpu','/target:winexe','/main:Dafeiyu.UpdateWorkerProgram','/define:UPDATE_WORKER',('/out:' + $workerDestination),('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) @references @localeAssets @workerSources
    if ($LASTEXITCODE -ne 0) { throw 'Update helper compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination ($workerDestination + '.config') -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination ($destination + '.config') -Force
if ($Target -ne 'App') {
    if ($TestArguments -contains '--compile-only') { Write-Output ('Compiled fixture without execution: '+$destination); return }
    & $destination @TestArguments
    if ($LASTEXITCODE -ne 0) { throw "Tests failed ($Target)." }
} else { Write-Output $destination }
