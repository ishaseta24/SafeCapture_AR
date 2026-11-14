// Copyright 2022-2026 Niantic Spatial.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NianticSpatial.NSDK.AR.Loader;
using NianticSpatial.NSDK.AR.Sites;
using UnityEngine;
using UnityEngine.UI;
using Input = NianticSpatial.NSDK.AR.Input;

namespace NianticSpatial.Nsdk.MetaQuest.InternalSamples
{
    public class SitesClientDemo : MonoBehaviour
    {
        // Use for the Dropdown menu so decide between Organization or Sites.
        private enum SitePickerPhase
        {
            None,
            NearbySites,
            PickOrganization,
            PickSiteInOrganization,
        }

        private readonly struct PickedSite
        {
            public readonly SiteInfo Site;
            public readonly AssetInfo[] Assets;
            public readonly double? DistanceMeters;

            public PickedSite(SiteInfo site, AssetInfo[] assets, double? distanceMeters)
            {
                Site = site;
                Assets = assets ?? Array.Empty<AssetInfo>();
                DistanceMeters = distanceMeters;
            }
        }

        [SerializeField, Tooltip("VPS sample that performs anchor tracking (no Sites API dependency).")]
        private VPS2LocalizationSample _vpsLocalizationSample;

        [SerializeField, Tooltip("Sites API client used to query nearby VPS-enabled sites")]
        private SitesClientManager _sitesClientManager;

        [Header("UI")]
        [SerializeField]
        private Dropdown _siteDropdown;

        [SerializeField]
        private Text _gpsDisplayText;

        [SerializeField]
        private Text _debugDisplayText;

        [SerializeField]
        private Text _statusDisplayText;

        [SerializeField, Tooltip("Filters the organization site list by name (PickSiteInOrganization only).")]
        private InputField _orgSiteSearchInput;

        [Header("Sites Query")]
        [SerializeField, Tooltip("Search radius around the user's location, in meters")]
        private double _searchRadiusMeters = 10000.0;

        private readonly List<PickedSite> _pickableSites = new();
        private readonly List<PickedSite> _displayedSites = new();
        private readonly List<OrganizationInfo> _organizations = new();
        private SitePickerPhase _pickerPhase = SitePickerPhase.None;
        private bool _usePlaceholderFirstOption;
        private bool _suppressDropdownEvents;
        private bool _ignoreOrgSiteSearchCallback;
        private string _siteDropdownPlaceholderText = "Select an Option";

        private void OnEnable()
        {
            _vpsLocalizationSample.LocalizationStopped += OnVpsLocalizationStopped;
            _siteDropdown.onValueChanged.AddListener(OnSiteDropdownValueChanged);
            _orgSiteSearchInput.onValueChanged.AddListener(OnOrgSiteSearchValueChanged);
        }

        private void OnDisable()
        {
            _vpsLocalizationSample.LocalizationStopped -= OnVpsLocalizationStopped;
            _siteDropdown.onValueChanged.RemoveListener(OnSiteDropdownValueChanged);
            _orgSiteSearchInput.onValueChanged.RemoveListener(OnOrgSiteSearchValueChanged);
        }

        private async void Start()
        {
            _debugDisplayText.text = string.Empty;

            if (!NsdkSettingsHelper.ActiveSettings.UsingAccessTokenOverride)
            {
                const string accessTokenOverrideWarning =
                    "NSDK on Meta requires Access Token to be set in NSDK Settings (XR Plug-in Management > Niantic Spatial Development Kit). ";
                Debug.LogWarning(accessTokenOverrideWarning);
                _debugDisplayText.text = accessTokenOverrideWarning;
            }

            try
            {
                await PopulateSitesAsync(destroyCancellationToken);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _debugDisplayText.text = $"Error loading sites: {e.Message}";
            }
        }

        private void OnVpsLocalizationStopped()
        {
            if (_pickerPhase == SitePickerPhase.PickSiteInOrganization)
            {
                BackToOrganizations();
            }
            else
            {
                _usePlaceholderFirstOption = _pickableSites.Count > 0;

                RebuildDropdown();
                _statusDisplayText.text =
                    $"Found {_pickableSites.Count} VPS site(s) — pick one or go back";
            }

            _siteDropdown.gameObject.SetActive(true);
        }

        private async Task PopulateSitesAsync(System.Threading.CancellationToken ct)
        {

            var (gpsOk, lat, lng) = await TryAcquireGpsAsync(ct);
            ct.ThrowIfCancellationRequested();

            if (gpsOk)
            {
                _gpsDisplayText.text = $"Latitude: {lat:F6}\nLongitude: {lng:F6}";
                _statusDisplayText.text = "Querying nearby sites...";

                var nearbyResult = await _sitesClientManager.GetSiteAssetsByLocationAsync(
                    lat, lng, _searchRadiusMeters, AssetType.VpsInfo, ct);

                ct.ThrowIfCancellationRequested();

                if (nearbyResult.Status == SitesRequestStatus.Success)
                {
                    _pickableSites.Clear();
                    foreach (var entry in nearbyResult.Entries)
                    {
                        if (entry.Assets != null && entry.Assets.Any(HasUsableVpsPayload))
                        {
                            _pickableSites.Add(new PickedSite(entry.Site, entry.Assets, entry.Distance));
                        }
                    }

                    if (_pickableSites.Count > 0)
                    {
                        _pickerPhase = SitePickerPhase.NearbySites;
                        _usePlaceholderFirstOption = true;
                        RebuildDropdown();
                        _statusDisplayText.text = $"Found {_pickableSites.Count} site(s) nearby";

                        return;
                    }

                    _debugDisplayText.text =
                        "No VPS-enabled production sites within search radius. Browsing your organizations... \n";
                }
                else
                {
                    _debugDisplayText.text =
                        $"Nearby sites query failed ({nearbyResult.Error}). Browsing your organizations... \n";
                }
            }
            else
            {
                _debugDisplayText.text =
                    " Browsing your organizations... \n";
                _gpsDisplayText.text = "Location Service: unavailable";
            }

            await LoadOrganizationsPhaseAsync(ct);
        }

        private static async Task<(bool acquired, double lat, double lng)> TryAcquireGpsAsync(
            System.Threading.CancellationToken ct)
        {
            if (!Input.location.isEnabledByUser)
            {
                return (false, 0, 0);
            }

            if (Input.location.status == LocationServiceStatus.Stopped)
            {
                Input.location.Start(500f, 500f);
            }

            var maxWait = 15;
            while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                await Task.Delay(1000, ct);
                maxWait--;
            }

            if (Input.location.status == LocationServiceStatus.Running)
            {
                var data = Input.location.lastData;
                return (true, data.latitude, data.longitude);
            }

            return (false, 0, 0);
        }

        private async Task LoadOrganizationsPhaseAsync(System.Threading.CancellationToken ct)
        {
            _statusDisplayText.text = "Loading organizations...";

            var orgsResult = await _sitesClientManager.GetSelfOrganizationInfoAsync(ct);
            ct.ThrowIfCancellationRequested();

            if (orgsResult.Status != SitesRequestStatus.Success)
            {
                _debugDisplayText.text = $"Could not load organizations: {orgsResult.Error}";
                _statusDisplayText.text = "Organizations unavailable";

                _pickerPhase = SitePickerPhase.None;
                ClearDropdownToEmpty();
                return;
            }

            _organizations.Clear();
            _organizations.AddRange(orgsResult.Organizations);

            if (_organizations.Count == 0)
            {
                _debugDisplayText.text =
                    "No organizations returned for this account. Check authentication and access.";
                _statusDisplayText.text = "No organizations found";

                _pickerPhase = SitePickerPhase.None;
                ClearDropdownToEmpty();
                return;
            }

            _pickerPhase = SitePickerPhase.PickOrganization;
            _pickableSites.Clear();
            _usePlaceholderFirstOption = true;
            RebuildDropdown();

            _statusDisplayText.text = $"Select an organization ({_organizations.Count})";
        }

        private async Task LoadSitesForOrganizationAsync(OrganizationInfo org, System.Threading.CancellationToken ct)
        {
            try
            {
                _siteDropdown.interactable = false;
                _statusDisplayText.text = $"Loading sites for {org.Name}...";

                var sitesResult = await _sitesClientManager.GetSitesForOrganizationAsync(org.Id, ct);
                ct.ThrowIfCancellationRequested();

                if (sitesResult.Status != SitesRequestStatus.Success || sitesResult.Sites.Length == 0)
                {
                    _debugDisplayText.text =
                        sitesResult.Status != SitesRequestStatus.Success
                            ? $"Could not load sites: {sitesResult.Error}"
                            : $"No sites returned for organization \"{org.Name}\".";
                    _statusDisplayText.text = "No sites for this organization";

                    BackToOrganizations();
                    return;
                }

                var fetchTasks = sitesResult.Sites.Select(async site =>
                {
                    try
                    {
                        var assetsResult = await _sitesClientManager.GetAssetsForSiteAsync(site.Id, ct);
                        if (assetsResult.Status != SitesRequestStatus.Success)
                        {
                            return (PickedSite?)null;
                        }

                        if (!assetsResult.Assets.Any(HasUsableVpsPayload))
                        {
                            return (PickedSite?)null;
                        }

                        return new PickedSite(site, assetsResult.Assets, null);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Failed to load assets for site {site.Name}: {e.Message}");
                        return (PickedSite?)null;
                    }
                }).ToList();

                var fetchResults = await Task.WhenAll(fetchTasks);
                ct.ThrowIfCancellationRequested();

                _pickableSites.Clear();
                foreach (var tuple in fetchResults)
                {
                    if (tuple.HasValue)
                    {
                        _pickableSites.Add(tuple.Value);
                    }
                }

                _pickableSites.Sort((a, b) =>
                    string.Compare(a.Site.Name, b.Site.Name, StringComparison.OrdinalIgnoreCase));

                if (_pickableSites.Count == 0)
                {
                    _debugDisplayText.text =
                        $"No VPS-enabled production sites with anchor payloads in \"{org.Name}\".";
                    _statusDisplayText.text = "No VPS sites in this organization";

                    BackToOrganizations();
                    return;
                }

                _pickerPhase = SitePickerPhase.PickSiteInOrganization;
                _usePlaceholderFirstOption = true;
                ClearOrgSiteSearchTextForNavigation();
                RebuildDropdown();

                _statusDisplayText.text = $"Found {_pickableSites.Count} VPS site(s) — pick one or go back";
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                _debugDisplayText.text = $"Error loading organization sites: {e.Message}";
                _statusDisplayText.text = "Error loading sites";

                if (!destroyCancellationToken.IsCancellationRequested)
                {
                    BackToOrganizations();
                }
            }
            finally
            {
                if (!destroyCancellationToken.IsCancellationRequested)
                {
                    _siteDropdown.interactable = _pickerPhase != SitePickerPhase.None;
                }
            }
        }

        private void BackToOrganizations()
        {
            _pickerPhase = SitePickerPhase.PickOrganization;
            _pickableSites.Clear();
            _usePlaceholderFirstOption = true;
            ClearOrgSiteSearchTextForNavigation();
            RebuildDropdown();

            _statusDisplayText.text = $"Select an organization ({_organizations.Count})";
        }

        private void ClearDropdownToEmpty()
        {
            _suppressDropdownEvents = true;
            try
            {
                _siteDropdown.ClearOptions();
                _siteDropdown.interactable = false;
                _siteDropdown.RefreshShownValue();
            }
            finally
            {
                _suppressDropdownEvents = false;
            }
        }

        private void RebuildDropdown()
        {
            _displayedSites.Clear();
            if (_pickerPhase == SitePickerPhase.PickSiteInOrganization)
            {
                var q = _orgSiteSearchInput.text.Trim();
                if (string.IsNullOrEmpty(q))
                {
                    _displayedSites.AddRange(_pickableSites);
                }
                else
                {
                    foreach (var p in _pickableSites)
                    {
                        if (p.Site.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _displayedSites.Add(p);
                        }
                    }
                }
            }
            else
            {
                _displayedSites.AddRange(_pickableSites);
            }

            _suppressDropdownEvents = true;
            try
            {
                _siteDropdown.ClearOptions();

                switch (_pickerPhase)
                {
                    case SitePickerPhase.NearbySites:
                        RebuildNearbySiteOptions();
                        break;
                    case SitePickerPhase.PickOrganization:
                        RebuildOrganizationOptions();
                        break;
                    case SitePickerPhase.PickSiteInOrganization:
                        RebuildOrgSiteOptions();
                        break;
                    default:
                        _siteDropdown.interactable = false;
                        _siteDropdown.RefreshShownValue();
                        break;
                }
            }
            finally
            {
                _suppressDropdownEvents = false;
            }

            _orgSiteSearchInput.gameObject.SetActive(_pickerPhase == SitePickerPhase.PickSiteInOrganization);
        }

        private void OnOrgSiteSearchValueChanged(string _)
        {
            if (_ignoreOrgSiteSearchCallback || _pickerPhase != SitePickerPhase.PickSiteInOrganization)
            {
                return;
            }

            RebuildDropdown();
        }

        private void ClearOrgSiteSearchTextForNavigation()
        {
            _ignoreOrgSiteSearchCallback = true;
            try
            {
                _orgSiteSearchInput.text = string.Empty;
            }
            finally
            {
                _ignoreOrgSiteSearchCallback = false;
            }
        }

        private void RebuildNearbySiteOptions()
        {
            if (_displayedSites.Count == 0)
            {
                _usePlaceholderFirstOption = false;
                _siteDropdown.interactable = false;
                _siteDropdown.RefreshShownValue();
                return;
            }

            var optionRows = new List<Dropdown.OptionData>();
            if (_usePlaceholderFirstOption)
            {
                optionRows.Add(new Dropdown.OptionData(string.IsNullOrEmpty(_siteDropdownPlaceholderText)
                    ? "Select an Option"
                    : _siteDropdownPlaceholderText));
            }

            optionRows.AddRange(_displayedSites.Select(e =>
            {
                var dist = e.DistanceMeters;
                var suffix = dist.HasValue ? $" ({dist.Value:F0} m)" : string.Empty;
                return new Dropdown.OptionData($"{e.Site.Name}{suffix}");
            }));

            _siteDropdown.AddOptions(optionRows);
            _siteDropdown.interactable = true;
            _siteDropdown.SetValueWithoutNotify(0);
            _siteDropdown.RefreshShownValue();
        }

        private void RebuildOrganizationOptions()
        {
            if (_organizations.Count == 0)
            {
                _siteDropdown.interactable = false;
                _siteDropdown.RefreshShownValue();
                return;
            }

            var optionRows = new List<Dropdown.OptionData>();
            if (_usePlaceholderFirstOption)
            {
                optionRows.Add(new Dropdown.OptionData(string.IsNullOrEmpty(_siteDropdownPlaceholderText)
                    ? "Select organization"
                    : _siteDropdownPlaceholderText));
            }

            optionRows.AddRange(_organizations.Select(o => new Dropdown.OptionData(o.Name)));
            _siteDropdown.AddOptions(optionRows);
            _siteDropdown.interactable = true;
            _siteDropdown.SetValueWithoutNotify(0);
            _siteDropdown.RefreshShownValue();
        }

        private void RebuildOrgSiteOptions()
        {
            if (_displayedSites.Count == 0)
            {
                _siteDropdown.ClearOptions();
                _siteDropdown.interactable = false;
                _siteDropdown.RefreshShownValue();
                return;
            }

            var optionRows = new List<Dropdown.OptionData>();
            if (_usePlaceholderFirstOption)
            {
                optionRows.Add(new Dropdown.OptionData(string.IsNullOrEmpty(_siteDropdownPlaceholderText)
                    ? "Select a site"
                    : _siteDropdownPlaceholderText));
            }

            optionRows.AddRange(_displayedSites.Select(e =>
            {
                var dist = e.DistanceMeters;
                var suffix = dist.HasValue ? $" ({dist.Value:F0} m)" : string.Empty;
                return new Dropdown.OptionData($"{e.Site.Name}{suffix}");
            }));
            _siteDropdown.AddOptions(optionRows);
            _siteDropdown.interactable = true;
            _siteDropdown.SetValueWithoutNotify(0);
            _siteDropdown.RefreshShownValue();
        }

        private void OnSiteDropdownValueChanged(int index)
        {
            if (_suppressDropdownEvents)
            {
                return;
            }

            HandleSiteDropdownSelection(index);
        }

        private void HandleSiteDropdownSelection(int index)
        {
            switch (_pickerPhase)
            {
                case SitePickerPhase.NearbySites:
                case SitePickerPhase.PickSiteInOrganization:
                {
                    if (TryResolvePlaceholderDropdownIndex(index, _displayedSites.Count, out var listIndex))
                    {
                        LocalizeToSelectedSite(_displayedSites[listIndex], listIndex);
                    }

                    break;
                }
                case SitePickerPhase.PickOrganization:
                {
                    if (TryResolvePlaceholderDropdownIndex(index, _organizations.Count, out var orgListIndex))
                    {
                        _ = LoadSitesForOrganizationAsync(
                            _organizations[orgListIndex], destroyCancellationToken);
                    }

                    break;
                }
            }
        }

        private bool TryResolvePlaceholderDropdownIndex(int dropdownIndex, int listCount, out int listIndex)
        {
            var hasPlaceholder = _usePlaceholderFirstOption && listCount > 0;
            if (hasPlaceholder && dropdownIndex == 0)
            {
                listIndex = 0;
                return false;
            }

            listIndex = hasPlaceholder ? dropdownIndex - 1 : dropdownIndex;
            return listIndex < listCount;
        }

        private static bool HasUsableVpsPayload(AssetInfo asset)
        {
            return asset.AssetType == AssetType.VpsInfo
                && asset.VpsData.HasValue
                && !string.IsNullOrEmpty(asset.VpsData.Value.AnchorPayload)
                && asset.Deployment == AssetDeploymentType.Production;
        }

        private void LocalizeToSelectedSite(PickedSite entry, int selectedSiteIndex)
        {
            var asset = entry.Assets.FirstOrDefault(HasUsableVpsPayload);
            var payload = asset.VpsData.Value.AnchorPayload;

            _statusDisplayText.text = $"Localizing to site: {entry.Site.Name}";

            bool started = _vpsLocalizationSample.SetAnchorId(payload);

            Debug.Log(
                $"Selected site: {entry.Site.Name}, " +
                $"payload length: {payload?.Length ?? 0}, " +
                $"tracking started: {started}");

            if (!started)
            {
                _statusDisplayText.text =
                    $"Failed to start localization for {entry.Site.Name}";
                return;
            }

            if (_pickerPhase == SitePickerPhase.NearbySites && _usePlaceholderFirstOption)
            {
                _usePlaceholderFirstOption = false;
                RebuildDropdown();
                if (selectedSiteIndex >= 0 && selectedSiteIndex < _displayedSites.Count)
                {
                    _suppressDropdownEvents = true;
                    try
                    {
                        _siteDropdown.SetValueWithoutNotify(selectedSiteIndex);
                        _siteDropdown.RefreshShownValue();
                    }
                    finally
                    {
                        _suppressDropdownEvents = false;
                    }
                }
            }

            _siteDropdown.gameObject.SetActive(false);
            _orgSiteSearchInput.gameObject.SetActive(false);
        }
    }
}
