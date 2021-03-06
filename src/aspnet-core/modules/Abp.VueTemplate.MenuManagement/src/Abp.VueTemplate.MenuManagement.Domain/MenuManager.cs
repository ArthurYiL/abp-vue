﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Services;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;

namespace Abp.VueTemplate.MenuManagement
{
    public class MenuManager : IMenuManager, ITransientDependency
    {
        private readonly IPermissionDefinitionManager _permissionDefinitionManager;
        private readonly IMenuGrantRepository _menuGrantRepository;
        private readonly IPermissionManager _permissionManager;
        private readonly ICurrentTenant _currentTenant;
        private readonly IReadOnlyList<IMenuManagementProvider> _managementProviders;
        private readonly MenuManagementOptions _options;

        public MenuManager(
            IPermissionDefinitionManager permissionDefinitionManager,
          IMenuGrantRepository menuGrantRepository,
            IPermissionManager permissionManager,
            IOptions<MenuManagementOptions> options,
            IServiceProvider serviceProvider,
            ICurrentTenant currentTenant)
        {
            _permissionDefinitionManager = permissionDefinitionManager;
            _menuGrantRepository = menuGrantRepository;
            _permissionManager = permissionManager;
            _currentTenant = currentTenant;
            _options = options.Value;

            _managementProviders = _options.ManagementProviders
                .Select(c => serviceProvider.GetRequiredService(c) as IMenuManagementProvider)
                .ToList();
        }

        public virtual IReadOnlyList<PermissionDefinition> GetPermissions(string providerName)
        {
            var multiTenancySide = _currentTenant.GetMultiTenancySide();

            var permissions = _permissionDefinitionManager.GetPermissions();
            return permissions.Where(x =>
            {
                if (x.Providers.Any() && !x.Providers.Contains(providerName))
                {
                    return false;
                }

                if (!x.MultiTenancySide.HasFlag(multiTenancySide))
                {
                    return false;
                }

                return true;
            }).ToImmutableList();
        }

        public virtual async Task<MenuWithGrantedProviders> GetAsync(Guid menuId, string providerName, string providerKey)
        {
            var result = new MenuWithGrantedProviders(menuId, false);

            foreach (var provider in _managementProviders)
            {
                var providerResult = await provider.CheckAsync(menuId, providerName, providerKey);
                if (providerResult.IsGranted)
                {
                    result.IsGranted = true;
                    result.Providers.Add(new MenuProviderInfo(provider.Name, providerResult.ProviderKey));
                }
            }

            return result;
        }

        public virtual async Task SetAsync(Guid menuId, string providerName, string providerKey, bool isGranted)
        {
            var currentGrantInfo = await GetAsync(menuId, providerName, providerKey);
            if (currentGrantInfo.IsGranted == isGranted)
            {
                return;
            }

            var provider = _managementProviders.FirstOrDefault(m => m.Name == providerName);
            if (provider == null)
            {
                throw new UserFriendlyException("Unknown menu management provider: " + providerName);
            }

            await provider.SetAsync(menuId, providerKey, isGranted);
        }

        public virtual async Task UpdatePermissionGrantAsync(Guid menuId, string oldPermission, string newPermission)
        {
            var menuGrants = await _menuGrantRepository.GetGrantByMenuIdAsync(menuId);
            foreach (var g in menuGrants)
            {
                if (!oldPermission.IsNullOrEmpty() && !newPermission.IsNullOrEmpty()) // 菜单原本有权限控制，修改为无权限控制：这种情况不清除授权，视为开放权限。
                {
                    await _permissionManager.SetAsync(oldPermission, g.ProviderName, g.ProviderKey, false);
                }

                if (!newPermission.IsNullOrEmpty())
                {
                    await _permissionManager.SetAsync(newPermission, g.ProviderName, g.ProviderKey, true);
                }
            }
        }
    }
}