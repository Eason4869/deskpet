# Live language migration inventory

Base: remote dev / v0.11.0-dev.1 = 574cad11. Target 0.11.0-dev.2.

This inventory retains the resource-read counts from the audited dev.1 baseline. Display recipes now use live delegates (including shared helpers); operation-time errors, external output and historical/exported content retain their existing evaluation boundaries. Running requests capture a LanguageSelection before awaiting. This file routes review by source file; behavioral evidence is recorded in live-language-acceptance.md.

CI warnings supplied by user: CA1416, CS0649, CS0618, nullable CS86xx, CS7022, CS9113. New code must not add these; compare baseline separately.

| File | Resource reads | Classification |
| --- | ---: | --- |
| `src/AboutPanel.cs` | 51 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ActionPaletteWindow.cs` | 32 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AdditionalInteractions.cs` | 7 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentApprovalSurface.cs` | 11 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AgentChanges.cs` | 20 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentChangeStatus.cs` | 1 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AgentChangeSummaryCard.cs` | 10 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AgentCommand.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentContextBudget.cs` | 11 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentDiffView.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AgentFactsPane.cs` | 16 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AgentFileTools.cs` | 11 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentGitTools.cs` | 27 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentImageInspection.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentJobsPanel.cs` | 26 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AgentKernel/AgentAccessPolicy.cs` | 26 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentAuthorizationService.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentChildTools.cs` | 6 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentCommandExecution.cs` | 9 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentExecutionReconciliation.cs` | 2 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentInstructions.cs` | 4 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentJobApplicationService.cs` | 2 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentJobManager.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentJobTools.cs` | 17 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentJobUiText.cs` | 16 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentPermission.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentProfile.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentRunJournal.cs` | 10 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentSkills.cs` | 18 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentSubagent.cs` | 4 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentSubagentRunner.cs` | 12 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentSubagentScheduler.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentTaskMemory.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentTaskRunner.cs` | 3 | request capture or explicit language-aware policy; historical output retained |
| `src/AgentKernel/AgentTerminalJournal.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentTerminalTools.cs` | 14 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentToolRuntime.cs` | 6 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/AgentWorkCatalog.cs` | 4 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/ConversationAgentJobs.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/ManagedSkillLibrary.cs` | 24 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/McpConnectionManager.cs` | 12 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/McpToolAdapter.cs` | 24 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/SessionModel.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/SessionStore.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/SkillArchive.cs` | 8 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/SkillRepositoryCatalog.cs` | 13 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/SkillRepositoryClient.cs` | 36 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentKernel/WindowsWorkspaceSandbox.cs` | 8 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentProjectTools.cs` | 14 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentQuestionController.cs` | 2 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentQuestionSurface.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AgentTaskPanel.cs` | 42 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AgentTaskState.cs` | 20 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentTaskStore.cs` | 7 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentWorkspace.cs` | 81 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AgentWorkspaceNavigation.cs` | 3 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AiConfigurationPanel.cs` | 7 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ApiClient.cs` | 38 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ApplicationRestart.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ApplicationSnapshots.cs` | 13 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ArchivedConversationsPanel.cs` | 15 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/AttachmentPicker.cs` | 10 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/AudioRecorder.cs` | 7 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/BlankPreviewPage.cs` | 14 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/BrowserPane.cs` | 23 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/BundledSkinCache.cs` | 8 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/CacheMaintenanceWindow.cs` | 14 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatAppShell.cs` | 26 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatAttachment.cs` | 11 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ChatChangeSummaries.cs` | 12 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatCompanionRoute.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatFilePreview.cs` | 10 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatHistoryNavigation.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatImagePreview.cs` | 34 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatMessageView.cs` | 42 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatStream.cs` | 6 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ChatUi.cs` | 9 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatUnifiedWorkspace.cs` | 13 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatWindow.cs` | 23 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ChatWorkspaceLayout.cs` | 18 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/CompanionConfigurationPanel.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/CompanionHomePage.cs` | 32 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/CompanionPackStorage.cs` | 20 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ComposerContextUsage.cs` | 4 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ComposerPermissions.cs` | 27 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ConnectionSettingsPanel.cs` | 56 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ConversationComposer.cs` | 137 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ConversationContextUsage.cs` | 2 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ConversationEntryNavigation.cs` | 5 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ConversationImageGeneration.cs` | 12 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ConversationOwnership.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ConversationProjectPicker.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ConversationQueue.cs` | 23 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ConversationSession.cs` | 54 | request capture or explicit language-aware policy; historical output retained |
| `src/ConversationWorkspace.cs` | 7 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ConversationWorkspaceManager.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/DataActionConfirmationWindow.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/DataManagementPanel.cs` | 24 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/DeleteConversationDialog.cs` | 10 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/DialogUi.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/DictationSession.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/DocumentFormats.cs` | 21 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/FileActionsMenu.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/FilePreviewPane.cs` | 17 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/GeminiLiveSession.cs` | 21 | request capture or explicit language-aware policy; historical output retained |
| `src/GeneratedImageCard.cs` | 11 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/GeneratedImageGroupCard.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/GeneratedImageStore.cs` | 32 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/GeneratedImageStore.Management.cs` | 14 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/GitHubAssetTransport.cs` | 12 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/GitHubClient.cs` | 17 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/GitHubReleaseChannels.cs` | 14 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/GitHubUpdates.cs` | 16 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/GreetingBubbleWindow.cs` | 4 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/GreetingSchedule.cs` | 55 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/GreetingService.cs` | 2 | request capture or explicit language-aware policy; historical output retained |
| `src/HistoryStore.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/HolidayKeepsakeAlbumView.cs` | 21 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/HolidayOutfitPanel.cs` | 7 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/Holidays/HolidayController.cs` | 2 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Hotkeys.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/HoverActions.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/HtmlFilePreview.cs` | 5 | document/history/export rendering; original user and generated content retained |
| `src/IllustratedPack.cs` | 28 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageAssetReferenceResolver.cs` | 9 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageAssetUsageRegistry.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageDownloadSecurity.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageGalleryExport.cs` | 6 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageGalleryPanel.cs` | 14 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ImageGenerationClient.cs` | 32 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageGenerationMotion.cs` | 9 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageGenerationPendingCard.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ImageGenerationProtocols.cs` | 25 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageGenerationProvider.cs` | 2 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageGenerationSettings.cs` | 57 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ImageGenerationTask.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageGenerationTaskManager.cs` | 24 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageGenerationTaskStore.cs` | 10 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageServicePresets.cs` | 10 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageTaskCenterPage.cs` | 33 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ImageTaskCenterState.cs` | 15 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ImageWorkspacePage.cs` | 135 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/LanguagePickerWindow.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/LiveCallWindow.cs` | 19 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/LiveChangeSummary.cs` | 3 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/Maintenance/AttachmentArchive.cs` | 6 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Maintenance/CachePresentation.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Maintenance/ConversationMaintenance.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Maintenance/DataMaintenanceGate.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Maintenance/DataManagementPresentation.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Maintenance/HistoryMaintenanceService.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/MarkdownDocument.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/MarkdownLinkMenu.cs` | 4 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/McpSettingsPanel.cs` | 56 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/MessagePresentation.cs` | 4 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ModelCapabilityUi.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ModelConfigurationUi.cs` | 9 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/NativeAgentClient.cs` | 43 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OfficialCompanionDownloads.cs` | 7 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OfficialCompanionFeed.cs` | 9 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OfficialCompanionIdentity.cs` | 2 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OfficialCompanionModels.cs` | 13 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OfficialCompanionPackageValidation.cs` | 33 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OfficialCompanionPreparation.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OfficialCompanionProfiles.cs` | 44 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OfficialCompanionProgressWindow.cs` | 17 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/OfficialCompanionStorage.cs` | 18 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OnboardingBookends.cs` | 3 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/OnboardingGeometry.cs` | 1 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/OnboardingLessons.cs` | 286 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/OnboardingWindow.cs` | 67 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/PcmOutput.cs` | 6 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/PersonaPanel.cs` | 7 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/PersonaProfiles.cs` | 12 | request capture or explicit language-aware policy; historical output retained |
| `src/PetAgentJobServices.cs` | 7 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/PetAppShell.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/PetBusiness.cs` | 21 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/PetDataMaintenance.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/PetHoliday.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/PetImageServices.cs` | 4 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/PetOfficialCompanions.cs` | 17 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/PetPermissions.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/PetUninstall.cs` | 13 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/PetWindow.cs` | 64 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/PluginsPage.cs` | 51 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/PreviewWorkspace.cs` | 8 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ProcessAvatar.cs` | 1 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ProjectCatalog.cs` | 23 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ProjectSidebar.cs` | 50 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ProjectSidebarActions.cs` | 7 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ProjectSidebarChatSections.cs` | 10 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ProjectSidebarLayout.cs` | 22 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ProjectSidebarOrdering.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ProviderPickerWindow.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ProxyConfiguration.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/QuickChatWindow.cs` | 11 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/QuietPanel.cs` | 16 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/RelationshipCardRenderer.cs` | 3 | document/history/export rendering; original user and generated content retained |
| `src/RelationshipFeedbackWindow.cs` | 3 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/RelationshipHomeVisual.cs` | 20 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/Relationships/HolidayKeepsakePresentation.cs` | 4 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Relationships/RelationshipCard.cs` | 8 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Relationships/RelationshipContent.cs` | 19 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Relationships/RelationshipPresentation.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Relationships/RelationshipRuntime.cs` | 7 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ReleaseIdentity.cs` | 4 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ReleaseVersion.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ReplyFileReference.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ReplyFiles.cs` | 9 | document/history/export rendering; original user and generated content retained |
| `src/ReplyLanguagePolicy.cs` | 1 | request capture or explicit language-aware policy; historical output retained |
| `src/SecretProtection.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ServicePresets.cs` | 14 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/Settings.cs` | 15 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/SettingsFeatureNavigation.cs` | 10 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/SettingsPage.cs` | 69 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/SettingsPageHeader.cs` | 3 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/SettingsWindow.cs` | 1 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ShellMenus.cs` | 37 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ShortcutCatalog.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ShortcutDefinitions.cs` | 40 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/ShortcutHelpDialog.cs` | 8 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ShortcutSettingsPanel.cs` | 13 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/SideChatPane.cs` | 17 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/SkillAddOverlay.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/SkillDiscoveryPanel.cs` | 64 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/SkillFolderImportPanel.cs` | 10 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/SkinCatalog.cs` | 25 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/SpeechOutput.cs` | 6 | request capture or explicit language-aware policy; historical output retained |
| `src/SpriteAtlas.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/TerminalBrowser.cs` | 2 | document/history/export rendering; original user and generated content retained |
| `src/TerminalPane.cs` | 16 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/TextProviderEditor.cs` | 65 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/ThemeModePicker.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/Ui.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/UiIcons.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/UiLanguages.cs` | 14 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/UninstallConfirmationWindow.cs` | 5 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/UninstallHandoff.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/UpdateCoordinator.cs` | 30 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/UpdateInstall.cs` | 35 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/UpdateNoticeWindow.cs` | 5 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/UpdateWorker.cs` | 6 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/UsageCharts.cs` | 36 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/UsageStatisticsPage.cs` | 63 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/UsageStore.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/VoicePresets.cs` | 36 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/WebPreviewRuntime.cs` | 2 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/WebPValidation.cs` | 2 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/WelcomeSuggestions.cs` | 10 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/WindowsCommandRunner.cs` | 4 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/WindowsTerminalSession.cs` | 8 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `src/WorkSettingsPanel.cs` | 123 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/WorkspaceImagePreview.cs` | 10 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `src/WpfOfficialNativeValidator.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `desktop/DeskPet.Desktop/AboutWindow.cs` | 18 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/CacheMaintenanceWindow.cs` | 14 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ChatMessageView.cs` | 25 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ChatWindow.cs` | 104 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ChatWindow.Work.cs` | 8 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/CompanionHomePage.cs` | 32 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/CompanionPreparationWindow.cs` | 5 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/DataActionConfirmationWindow.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/DataManagementView.cs` | 27 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/DesktopAgentJobsPanel.cs` | 25 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/DesktopCompanions.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/DesktopController.AgentJobs.cs` | 7 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/DesktopController.cs` | 19 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/DesktopWorkApproval.cs` | 5 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/DesktopWorkspaces.cs` | 3 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `desktop/DeskPet.Desktop/GeneratedImageCard.cs` | 57 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/GeneratedImageGroupCard.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/GitHubRoutingView.cs` | 5 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/GuideWindow.cs` | 69 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/HolidayKeepsakeAlbumView.cs` | 21 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/HolidayOutfitPanel.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ImageGenerationPendingCard.cs` | 5 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ImageProviderEditor.cs` | 38 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ImageTaskCenterPage.cs` | 33 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ImageWorkspacePage.cs` | 132 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ImageWorkspacePreview.cs` | 3 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/LanguagePickerWindow.cs` | 6 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/LiveWindow.cs` | 12 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/MarkdownView.cs` | 1 | document/history/export rendering; original user and generated content retained |
| `desktop/DeskPet.Desktop/NativeAttachmentPicker.cs` | 5 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `desktop/DeskPet.Desktop/OfficialCompanionNativeValidator.cs` | 1 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `desktop/DeskPet.Desktop/OfficialCompanionProgressView.cs` | 18 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/PetHoliday.cs` | 1 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/PetWindow.cs` | 21 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ProcessExpander.cs` | 1 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/ProviderEditor.cs` | 30 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/RelationshipCardRenderer.cs` | 3 | document/history/export rendering; original user and generated content retained |
| `desktop/DeskPet.Desktop/RelationshipFeedbackWindow.cs` | 3 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/RelationshipHomeVisual.cs` | 21 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/SettingsWindow.cs` | 81 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/SpriteAssets.cs` | 50 | operation-time validation/model text or delegated UI helper; no language-triggered operation replay |
| `desktop/DeskPet.Desktop/ThemeModePicker.cs` | 4 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/Ui.cs` | 2 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/WorkProjectPicker.cs` | 18 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
| `desktop/DeskPet.Desktop/WorkSettingsView.cs` | 36 | live display recipes and helper delegates; remaining reads evaluated at action/state boundary |
