using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.ObjectMapping;

namespace Lion.AbpPro.MasterDataManagement.MasterDatas
{
    public class MasterDataManager : DomainService
    {
        private readonly IMasterDataRepository _masterDataRepository;
        private readonly IObjectMapper _objectMapper;
        private readonly ICurrentTenant _currentTenant;

        public MasterDataManager(
            IMasterDataRepository masterDataRepository,
            IObjectMapper objectMapper,
            ICurrentTenant currentTenant
        )
        {
            _masterDataRepository = masterDataRepository;
            _objectMapper = objectMapper;
            _currentTenant = currentTenant;
        }

        /// <summary>
        /// 创建主数据
        /// </summary>
        public async Task<MasterDataDto> CreateAsync(
            Guid id,
            Guid masterDataTypeId,
            string name,
            string code,
            bool enabled,
            Dictionary<string, string> attributes = null
        )
        {
            // 检查编码是否已存在
            if (await _masterDataRepository.ExistsAsync(code))
            {
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataCodeExists
                );
            }

            var entity = new MasterData(
                id,
                masterDataTypeId,
                name,
                code,
                enabled,
                _currentTenant.Id
            );
            entity = await _masterDataRepository.InsertAsync(entity);
            return entity.Adapt<MasterDataDto>();
        }

        /// <summary>
        /// 更新主数据
        /// </summary>
        public async Task<MasterDataDto> UpdateAsync(
            Guid id,
            Guid masterDataTypeId,
            string name,
            string code
        )
        {
            // 检查编码是否已存在（排除当前记录）
            if (await _masterDataRepository.ExistsAsync(code, id))
            {
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataCodeExists
                );
            }

            var entity = await _masterDataRepository.FindAsync(id);
            if (entity == null)
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataCodeExists
                );
            entity.Update(masterDataTypeId, name, code);
            entity = await _masterDataRepository.UpdateAsync(entity);
            return entity.Adapt<MasterDataDto>();
        }

        /// <summary>
        /// 删除主数据
        /// </summary>
        public async Task DeleteAsync(Guid id)
        {
            var entity = await _masterDataRepository.FindAsync(id);
            if (entity == null)
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataCodeExists
                );
            await _masterDataRepository.DeleteAsync(entity);
        }

        /// <summary>
        /// 设置主数据启用/禁用状态
        /// </summary>
        public async Task EnabledAsync(Guid id, bool enabled)
        {
            var entity = await _masterDataRepository.FindAsync(id);
            if (entity == null)
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataCodeExists
                );
            entity.SetEnabled(enabled);
            await _masterDataRepository.UpdateAsync(entity);
        }

        /// <summary>
        /// 根据编码获取主数据
        /// </summary>
        public async Task<MasterDataDto> GetByIdAsync(Guid id)
        {
            var entity = await _masterDataRepository.FindAsync(id);
            return entity.Adapt<MasterDataDto>();
        }

        /// <summary>
        /// 根据编码获取主数据
        /// </summary>
        public async Task<MasterDataDto> GetByCodeAsync(string code)
        {
            var entity = await _masterDataRepository.GetByCodeAsync(code);
            return entity.Adapt<MasterDataDto>();
        }

        /// <summary>
        /// 根据主数据类型Id删除主数据
        /// </summary>
        public async Task DeleteByMasterDataTypeIdAsync(Guid masterDataTypeId)
        {
            await _masterDataRepository.DeleteByMasterDataTypeIdAsync(masterDataTypeId);
        }
    }
}
