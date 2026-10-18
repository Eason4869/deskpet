param([ValidateSet('App','AgentEvolutionRegressionTests','AgentTaskRunnerTests','McpUiSmoke','AgentIsolationTests','AgentCodingWorkflowTests','AgentTaskMemoryTests','McpConnectionTests','AgentRunRecoveryTests','AgentSubagentExecutionTests','AgentInstructionTests','AgentContextPolicyTests','AgentTerminalTests','AgentApprovalUiSmoke','AgentApprovalTests','AgentCapabilityTests','PetRuntimeTests','GuidanceUiTests','ComposerRenderingTests','DeleteDialogTests','UiResourceTests','QueueHandleTests','ConversationVisualTests','ConversationOwnershipUiTests','WelcomeSuggestionTests','DetailedGuideTests','ConversationModeUiTests','ConversationOwnershipTests','SidebarActionTests','SidebarProjectInteractionTests','SettingsMaximizeTests','ModelConfigurationSmoke','ContextUsageTests','DevReviewRegressionTests','SidebarSectionsTests','DraftConversationTests','ComposerPaletteTests','CoreTests','ApiTests','ProfileTests','ConversationTests','ChatSmoke','AudioSmoke','UiSmoke','MotionSmoke','LiveTests','UpdateTests','StartupTests','ReleaseUiSmoke','ProxyTests','GreetingTests','UpdateApplyTests','MenuSmoke','AutoUpdateSmoke','HistoryTests','StreamingTests','QuietTests','FeatureUiSmoke','VoicePresetTests','InteractionTests','SpriteAlignmentTests','InteractionUiSmoke','ActionPaletteSmoke','SettingsNavigationSmoke','OnboardingSmoke','OnboardingModelSmoke','LiveCallSmoke','GuideMenuSmoke','QueueTests','ComposerSmoke','DictationTests','AboutSmoke','SteeringSmoke','FeedbackSmoke','ProviderTests','ProviderUiSmoke','ImportedSkinTests','XiaobeiUiSmoke','AiGreetingTests','AiGreetingUiSmoke','BundledSkinTests','SkinPackExport','StumbleSmoke','HoverSleepSmoke','OnboardingUpgradeTests','RecorderFailureTests','ConversationUiSmoke','MarkdownTests','AgentWorkspaceTests','AgentProtocolTests','AgentSessionTests','ApplicationSnapshotTests','AttachmentPickerTests','WorkSettingsTests','UpdateScheduleTests','AgentReliabilityTests','AgentFileToolsTests','AgentChangesTests','AgentDocumentTests','AgentCommandTests','AgentTaskTests','AgentWorkbenchSmoke','AgentContextTests','PredictionTests','AgentDiffTests','AgentChangePreviewTests','ProjectCatalogTests','ConversationWorkspaceTests','ConversationWorkspaceManagerTests','ProjectSidebarSmoke','GitHubToolsTests','AgentSessionModelTests','AgentKernelTests','AgentKernelIntegrationTests','AgentTaskBindingTests','AgentContextRecoveryTests','AgentProfileTests','AgentProviderKernelTests','AgentSubagentToolTests','AgentIntegrationFinalTests','AgentKernelCompletenessTests','ConversationSingleWindowSmoke','ConversationRepeatSmoke','ConversationTitleTests')][string]$Target = 'App', [string]$OutputDirectory = '', [string[]]$TestArguments = @())
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
    if ($Target -eq 'SettingsMaximizeTests') { $options += '/win32manifest:' + (Join-Path $projectRoot 'app.manifest') }
}
# Shared business/interfaces are also needed by the small legacy test targets.
foreach ($sharedSource in @('PetBusiness.cs','ChatAttachment.cs','MarkdownContent.cs','AgentWorkspace.cs','AgentToolCatalog.cs','AgentFileTools.cs','AgentChanges.cs','AgentLineChanges.cs','DocumentFormats.cs','AgentCommand.cs','WindowsCommandRunner.cs','AgentTaskState.cs','AgentTaskStore.cs','AgentContextBudget.cs','ApiClient.cs','ChatStream.cs','NativeAgentClient.cs','AudioContracts.cs','SecretProtection.cs','OnboardingUpgrade.cs')) {
    $sharedPath = Join-Path $projectRoot ('src\' + $sharedSource)
    if ((Test-Path -LiteralPath $sharedPath) -and $sources -notcontains $sharedPath) { $sources += $sharedPath }
}
$contextShared=Join-Path $projectRoot 'src/ConversationContextUsage.cs'
if (($sources | Where-Object { [IO.Path]::GetFileName($_) -eq 'ConversationSession.cs' }) -and $sources -notcontains $contextShared) { $sources += $contextShared }
$predictionShared = Join-Path $projectRoot 'src/ConversationPredictions.cs'
foreach ($kernelSource in @((Join-Path $projectRoot 'src/AgentDiff.cs'),(Join-Path $projectRoot 'src/GitHubClient.cs')) + @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src/AgentKernel') -Filter '*.cs' | Select-Object -ExpandProperty FullName)) {
    if ($sources -notcontains $kernelSource) { $sources += $kernelSource }
}
if (($sources | Where-Object { [IO.Path]::GetFileName($_) -eq 'ConversationSession.cs' }) -and $sources -notcontains $predictionShared) { $sources += $predictionShared }
$assets = @('/resource:' + (Join-Path $projectRoot 'assets\provider-logos.json') + ',Dafeiyu.ProviderLogos')
if ($sources | Where-Object { [IO.Path]::GetFileName($_) -eq 'MarkdownContent.cs' }) {
    foreach ($markdownLibrary in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'vendor/markdown') -Filter '*.dll') {
        $references += '/reference:' + $markdownLibrary.FullName
        $assets += '/resource:' + $markdownLibrary.FullName + ',Dafeiyu.Markdown.' + $markdownLibrary.Name
    }
    foreach ($markdownLicense in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'vendor/markdown') -Filter '*LICENSE.txt') {
        $assets += '/resource:' + $markdownLicense.FullName + ',Dafeiyu.Markdown.' + $markdownLicense.Name
    }
}
foreach ($asset in @(@('spritesheet.png','Dafeiyu.Atlas'),@('pet.ico','Dafeiyu.Icon'),@('pet-preview-hd.png','Dafeiyu.Preview'),@('e-bao-spritesheet.png','Dafeiyu.EbaoAtlas'),@('e-bao-pet-preview-hd.png','Dafeiyu.EbaoPreview'),@('marfu-spritesheet.png','Dafeiyu.MarfuAtlas'),@('marfu-pet-preview-hd.png','Dafeiyu.MarfuPreview'),@('e-bao-interactions.png','Dafeiyu.EbaoInteractions'),@('dafeiyu-interactions.png','Dafeiyu.DafeiyuInteractions'),@('marfu-interactions.png','Dafeiyu.MarfuInteractions'))) {
    $assetPath = Join-Path $projectRoot ('assets\' + $asset[0])
    if (!(Test-Path -LiteralPath $assetPath)) { throw "Required artwork is missing: $assetPath" }
    $assets += '/resource:' + $assetPath + ',' + $asset[1]
}
foreach ($avatarId in @('e-bao','dafeiyu','marfu','xiaobei')) {
    $avatarPath = Join-Path $projectRoot ('assets\avatars\' + $avatarId + '.png')
    if (!(Test-Path -LiteralPath $avatarPath)) { throw "Required companion avatar is missing: $avatarPath" }
    $assets += '/resource:' + $avatarPath + ',Dafeiyu.Avatar.' + $avatarId
}
# Reference-matched eight-pose actions are required for all four presets.
foreach ($extraPet in @('e-bao','dafeiyu','marfu','xiaobei')) {
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
$bundledSkinRoot = Join-Path $projectRoot 'assets\bundled-skins\xiaobei'
if (Test-Path -LiteralPath $bundledSkinRoot -PathType Container) {
    $bundledResourceNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($bundledAsset in Get-ChildItem -LiteralPath $bundledSkinRoot -Recurse -File | Sort-Object FullName) {
        $bundledRelative = $bundledAsset.FullName.Substring($bundledSkinRoot.Length + 1)
        if ($bundledRelative -ne 'pet.json' -and $bundledAsset.Extension -ine '.png') { continue }
        $bundledResource = 'Dafeiyu.Bundled.Xiaobei.' + $bundledRelative.Replace('\','.').Replace('/','.')
        if (!$bundledResourceNames.Add($bundledResource)) { throw "Bundled preset resource collision: $bundledRelative" }
        $assets += '/resource:' + $bundledAsset.FullName + ',' + $bundledResource
    }
}
$arguments = @('/nologo','/optimize+','/codepage:65001','/utf8output','/platform:anycpu',('/out:' + $destination)) + $references + $options + $assets + $sources
$responseFile = Join-Path $workRoot ($Target + '.rsp')
$responseLines = @($arguments | ForEach-Object { '"' + ($_ -replace '(?<!\\)"','\"') + '"' })
[IO.File]::WriteAllLines($responseFile, $responseLines, [Text.UTF8Encoding]::new($true))
& $compiler ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($Target)." }
if ($Target -eq 'App') {
    $workerDestination = Join-Path $outputRoot 'DeskPet.Update.exe'
    $workerSources = @('UpdateInstall.cs','UpdateWorker.cs','ReleaseInfo.cs') | ForEach-Object { Join-Path $projectRoot ('src\' + $_) }
    & $compiler @('/nologo','/optimize+','/codepage:65001','/utf8output','/platform:anycpu','/target:winexe','/main:Dafeiyu.UpdateWorkerProgram','/define:UPDATE_WORKER',('/out:' + $workerDestination),('/win32manifest:' + (Join-Path $projectRoot 'app.manifest'))) @references @workerSources
    if ($LASTEXITCODE -ne 0) { throw 'Update helper compilation failed.' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination ($workerDestination + '.config') -Force
}
Copy-Item -LiteralPath (Join-Path $projectRoot 'app.config') -Destination ($destination + '.config') -Force
if ($Target -ne 'App') {
    & $destination @TestArguments
    if ($LASTEXITCODE -ne 0) { throw "Tests failed ($Target)." }
} else { Write-Output $destination }
