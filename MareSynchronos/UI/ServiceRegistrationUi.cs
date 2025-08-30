using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using MareSynchronos.MareConfiguration;
using MareSynchronos.MareConfiguration.Models;
using MareSynchronos.Services;
using MareSynchronos.Services.Mediator;
using MareSynchronos.Services.ServerConfiguration;
using MareSynchronos.WebAPI.Services;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Text.RegularExpressions;

namespace MareSynchronos.UI;

public partial class ServiceRegistrationUi : WindowMediatorSubscriberBase
{
    private readonly MareConfigService _configService;
    private readonly ServerConfigurationManager _serverConfigurationManager;
    private readonly UiSharedService _uiShared;
    private readonly RegistrationService _registrationService;

    private string _secretKey = string.Empty;
    private bool _useLegacyLogin = false;

    private bool _registrationInProgress = false;
    private bool _registrationSuccess = false;
    private string? _registrationMessage;

    public ServiceRegistrationUi(ILogger<ServiceRegistrationUi> logger, UiSharedService uiShared, MareConfigService configService,
        RegistrationService registrationService, ServerConfigurationManager serverConfigurationManager, MareMediator mareMediator,
        PerformanceCollectorService performanceCollectorService, DalamudUtilService dalamudUtilService)
        : base(logger, mareMediator, "Mare Synchronos Service Setup", performanceCollectorService)
    {
        _uiShared = uiShared;
        _configService = configService;
        _serverConfigurationManager = serverConfigurationManager;
        _registrationService = registrationService;

        AllowClickthrough = false;
        AllowPinning = false;
        IsOpen = false;

        SizeConstraints = new WindowSizeConstraints()
        {
            MinimumSize = new Vector2(600, 400),
            MaximumSize = new Vector2(600, 2000),
        };

        Mediator.Subscribe<SwitchToMainUiMessage>(this, (_) => IsOpen = false);
        Mediator.Subscribe<SwitchToServiceRegistrationUiMessage>(this, (_) =>
        {
            IsOpen = true;
        });
    }

    private int _prevIdx = -1;

    protected override void DrawInternal()
    {
        if (!_uiShared.ApiController.IsConnected)
        {
            using (_uiShared.UidFont.Push())
                ImGui.TextUnformatted("Service Registration");
            ImGui.Separator();
            UiSharedService.TextWrapped("To be able to use Mare Synchronos you will have to register an account.");
            UiSharedService.TextWrapped("Once you have registered you can connect to the service using the tools provided below.");

            int serverIdx = _serverConfigurationManager.CurrentServerIndex;

            using (var node = ImRaii.TreeNode("Advanced Options"))
            {
                if (node)
                {
                    serverIdx = _uiShared.DrawServiceSelection(selectOnChange: true, showConnect: false);
                    if (serverIdx != _prevIdx)
                    {
                        _uiShared.ResetOAuthTasksState();
                        _prevIdx = serverIdx;
                    }

                    var selectedServer = _serverConfigurationManager.GetServerByIndex(serverIdx);
                    _useLegacyLogin = !selectedServer.UseOAuth2;

                    if (ImGui.Checkbox("Use Legacy Login with Secret Key", ref _useLegacyLogin))
                    {
                        _serverConfigurationManager.GetServerByIndex(serverIdx).UseOAuth2 = !_useLegacyLogin;
                        _serverConfigurationManager.Save();
                    }
                }
            }

            if (_useLegacyLogin)
            {
                var text = "Enter Secret Key";
                var buttonText = "Save";
                var buttonWidth = _secretKey.Length != 64 ? 0 : ImGuiHelpers.GetButtonSize(buttonText).X + ImGui.GetStyle().ItemSpacing.X;
                var textSize = ImGui.CalcTextSize(text);

                var storedSecretKey = _serverConfigurationManager.GetSecretKey(out bool multi);
                if (multi)
                {
                    ImGuiHelpers.ScaledDummy(5);
                    UiSharedService.DrawGroupedCenteredColorText("Character has multile secret keys stored. Check the plugin settings.", ImGuiColors.DalamudYellow, 500);
                    ImGuiHelpers.ScaledDummy(5);
                    return;
                }

                if (_registrationSuccess && !string.IsNullOrEmpty(storedSecretKey))
                {
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted(text);
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonWidth - textSize.X);
                    ImGui.InputText("", ref storedSecretKey, 64, ImGuiInputTextFlags.ReadOnly);
                    return;
                }

                ImGuiHelpers.ScaledDummy(5);
                UiSharedService.DrawGroupedCenteredColorText("Strongly consider to use OAuth2 to authenticate, if the server supports it. " +
                    "The authentication flow is simpler and you do not require to store or maintain Secret Keys.", ImGuiColors.DalamudYellow, 500);
                ImGuiHelpers.ScaledDummy(5);

                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(text);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(UiSharedService.GetWindowContentRegionWidth() - ImGui.GetWindowContentRegionMin().X - buttonWidth - textSize.X);
                ImGui.InputText("", ref _secretKey, 64);
                if (_secretKey.Length > 0 && _secretKey.Length != 64)
                {
                    UiSharedService.ColorTextWrapped("Your secret key must be exactly 64 characters long.", ImGuiColors.DalamudRed);
                }
                else if (_secretKey.Length == 64 && !HexRegex().IsMatch(_secretKey))
                {
                    UiSharedService.ColorTextWrapped("Your secret key can only contain ABCDEF and the numbers 0-9.", ImGuiColors.DalamudRed);
                }
                else if (_secretKey.Length == 64)
                {
                    ImGui.SameLine();
                    if (ImGui.Button(buttonText))
                    {
                        if (_serverConfigurationManager.CurrentServer == null) _serverConfigurationManager.SelectServer(0);
                        if (!_serverConfigurationManager.CurrentServer!.SecretKeys.Any())
                        {
                            _serverConfigurationManager.CurrentServer!.SecretKeys.Add(_serverConfigurationManager.CurrentServer.SecretKeys.Select(k => k.Key).LastOrDefault() + 1, new SecretKey()
                            {
                                FriendlyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})",
                                Key = _secretKey,
                            });
                            _serverConfigurationManager.AddCurrentCharacterToServer();
                        }
                        else
                        {
                            _serverConfigurationManager.CurrentServer!.SecretKeys[0] = new SecretKey()
                            {
                                FriendlyName = $"Secret Key added on Setup ({DateTime.Now:yyyy-MM-dd})",
                                Key = _secretKey,
                            };
                        }
                        _secretKey = string.Empty;
                        _ = Task.Run(() => _uiShared.ApiController.CreateConnectionsAsync());
                    }
                }
                else
                {
                    this.DrawRegistration(serverIdx);
                }
            }
            else
            {
                DrawDiscordAuth(serverIdx);
            }
        }
        else
        {
            Mediator.Publish(new SwitchToMainUiMessage());
            IsOpen = false;
        }
    }

    private void DrawRegistration(int serverId)
    {
        var selectedServer = _serverConfigurationManager?.GetServerByIndex(serverId) ?? null;
        if(selectedServer is null)
        {
            Mediator.Publish(new OpenSettingsUiMessage());
            IsOpen = false;
            return;
        }

        ImGui.BeginDisabled(_registrationInProgress || _registrationSuccess || _secretKey.Length > 0);
        ImGui.Separator();
        ImGui.TextUnformatted($"If you have not used {selectedServer.ServerName} before, click below to register a new account.");
        if (_uiShared.IconTextButton(FontAwesomeIcon.Plus, "Register a new account"))
        {
            _registrationInProgress = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    var registrationReply = await _registrationService.RegisterAccount(CancellationToken.None).ConfigureAwait(false);
                    if (!registrationReply.Success)
                    {
                        _logger.LogWarning("Registration failed: {err}", registrationReply.ErrorMessage);
                        _registrationMessage = registrationReply.ErrorMessage ?? string.Empty;
                        if (string.IsNullOrEmpty(_registrationMessage))
                        {
                            _registrationMessage = "An unknown error occured. Please try again later.";
                        }
                        return;
                    }
                    _registrationMessage = "New account registered.\nPlease keep a copy of your secret key in case you need to reset your plugins, or to use it on another PC.";
                    _secretKey = registrationReply.SecretKey ?? "";
                    _registrationSuccess = true;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Registration failed");
                    _registrationSuccess = false;
                    _registrationMessage = "An unknown error occured. Please try again later.";
                }
                finally
                {
                    _registrationInProgress = false;
                }
            });
        }
        ImGui.EndDisabled();
        if (_registrationInProgress)
        {
            ImGui.TextUnformatted("Sending request...");
        }
        else if (!string.IsNullOrEmpty(_registrationMessage))
        {
            if (!_registrationSuccess)
                ImGui.TextColored(ImGuiColors.DalamudYellow, _registrationMessage);
            else
                ImGui.TextWrapped(_registrationMessage);
        }
    }

    private void DrawDiscordAuth(int serverId)
    {
        var selectedServer = _serverConfigurationManager?.GetServerByIndex(serverId) ?? null;
        if (selectedServer is null)
        {
            Mediator.Publish(new OpenSettingsUiMessage());
            IsOpen = false;
            return;
        }

        _uiShared.DrawHelpText("Use Discord OAuth2 Authentication to identify with this server instead of secret keys");
            _uiShared.DrawOAuth(selectedServer);
            if (string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer)))
            {
                ImGuiHelpers.ScaledDummy(10f);
                UiSharedService.ColorTextWrapped("You have enabled OAuth2 but it is not linked. Press the buttons Check, then Authenticate to link properly.", ImGuiColors.DalamudRed);
            }
        if (!string.IsNullOrEmpty(_serverConfigurationManager.GetDiscordUserFromToken(selectedServer))
            && selectedServer.Authentications.TrueForAll(u => string.IsNullOrEmpty(u.UID)))
        {
            ImGuiHelpers.ScaledDummy(10f);
            UiSharedService.ColorTextWrapped("You have enabled OAuth2 but no characters configured. Set the correct UIDs for your characters in \"Character Management\".",
                ImGuiColors.DalamudRed);
        }
    }

    [GeneratedRegex("^([A-F0-9]{2})+")]
    private static partial Regex HexRegex();
}