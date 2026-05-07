using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.ObjectMapping;

namespace Lion.AbpPro.MasterDataManagement.MasterDataTypes
{
    public class MasterDataTypeManager : DomainService
    {
        private readonly IMasterDataTypeRepository _masterDataTypeRepository;
        private readonly IObjectMapper _objectMapper;
        private readonly ICurrentTenant _currentTenant;

        public MasterDataTypeManager(
            IMasterDataTypeRepository masterDataTypeRepository,
            IObjectMapper objectMapper,
            ICurrentTenant currentTenant
        )
        {
            _masterDataTypeRepository = masterDataTypeRepository;
            _objectMapper = objectMapper;
            _currentTenant = currentTenant;
        }

        public async Task<List<MasterDataTypeDto>> GetListAsync(
            DateTime? startDateTime = null,
            DateTime? endDateTime = null,
            int maxResultCount = 10,
            int skipCount = 0
        )
        {
            var list = await _masterDataTypeRepository.GetListAsync(
                startDateTime,
                endDateTime,
                maxResultCount,
                skipCount
            );
            return list.Adapt<List<MasterDataTypeDto>>();
        }

        public async Task<long> GetCountAsync(
            DateTime? startDateTime = null,
            DateTime? endDateTime = null
        )
        {
            return await _masterDataTypeRepository.GetCountAsync(startDateTime, endDateTime);
        }

        /// <summary>
        /// 检查主数据类型是否存在
        /// </summary>
        public async Task<bool> ExistsAsync(Guid id)
        {
            return await _masterDataTypeRepository.ExistsAsync(id);
        }

        /// <summary>
        /// 根据编码查找主数据类型
        /// </summary>
        public async Task<MasterDataTypeDto> FindByCodeAsync(string code)
        {
            var entity = await _masterDataTypeRepository.FindByCodeAsync(code);
            if (entity == null)
                return null;
            return entity.Adapt<MasterDataTypeDto>();
        }

        /// <summary>
        /// 创建主数据类型
        /// </summary>
        public async Task<MasterDataTypeDto> CreateAsync(Guid id, string name, string code)
        {
            var exists = await _masterDataTypeRepository.ExistsAsync(code);
            if (exists)
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataTypeCodeExists
                );

            var entity = new MasterDataType(id, name, code, _currentTenant.Id);
            entity = await _masterDataTypeRepository.InsertAsync(entity);
            return entity.Adapt<MasterDataTypeDto>();
        }

        /// <summary>
        /// 更新主数据类型
        /// </summary>
        public async Task<MasterDataTypeDto> UpdateAsync(Guid id, string name, string code)
        {
            var exists = await _masterDataTypeRepository.ExistsAsync(code, id);
            if (exists)
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataTypeCodeExists
                );
            var entity = await _masterDataTypeRepository.FindAsync(id);
            if (entity == null)
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataTypeCodeExists
                );
            entity.Update(name, code);
            entity = await _masterDataTypeRepository.UpdateAsync(entity);
            return entity.Adapt<MasterDataTypeDto>();
        }

        /// <summary>
        /// 删除主数据类型
        /// </summary>
        public async Task DeleteAsync(Guid id)
        {
            var entity = await _masterDataTypeRepository.FindAsync(id);
            if (entity == null)
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataTypeCodeExists
                );
            await _masterDataTypeRepository.DeleteAsync(entity);
        }

        /// <summary>
        /// 根据编码删除主数据类型
        /// </summary>
        public async Task DeleteByCodeAsync(string code)
        {
            var entity = await _masterDataTypeRepository.FindByCodeAsync(code);
            if (entity == null)
                throw new MasterDataManagementException(
                    MasterDataManagementErrorCodes.MasterDataTypeCodeExists
                );
            await _masterDataTypeRepository.DeleteAsync(entity);
        }
    }
}
