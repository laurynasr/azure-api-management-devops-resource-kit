﻿// --------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation. All rights reserved.
//  Licensed under the MIT License.
// --------------------------------------------------------------------------

using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Constants;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Extractor.EntityExtractors.Abstractions;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Templates.Abstractions;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Extractor.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.API.Clients.Abstractions;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Extensions;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Templates.Builders.Abstractions;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Templates.Products;
using Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Templates.ProductApis;

namespace Microsoft.Azure.Management.ApiManagement.ArmTemplates.Extractor.EntityExtractors
{
    public class ProductExtractor : IProductExtractor
    {
        readonly ILogger<ProductExtractor> logger;
        readonly ITemplateBuilder templateBuilder;

        readonly IGroupsClient groupsClient;
        readonly IProductsClient productsClient;
        readonly ITagClient tagClient;

        readonly IPolicyExtractor policyExtractor;

        public ProductExtractor(
            ILogger<ProductExtractor> logger,
            IPolicyExtractor policyExtractor,
            IProductsClient productsClient,
            IGroupsClient groupsClient,
            ITagClient tagClient,
            ITemplateBuilder templateBuilder)
        {
            this.logger = logger;
            this.templateBuilder = templateBuilder;

            this.productsClient = productsClient;
            this.groupsClient = groupsClient;
            this.tagClient = tagClient;

            this.policyExtractor = policyExtractor;
            this.templateBuilder = templateBuilder;
        }

        public async Task<Template<ProductTemplateResources>> GenerateProductsTemplateAsync(
            string singleApiName,
            string baseFilesGenerationDirectory,
            ExtractorParameters extractorParameters)
        {
            var productsTemplate = this.templateBuilder
                                        .GenerateTemplateWithApimServiceNameProperty()
                                        .AddPolicyProperties(extractorParameters)
                                        .Build<ProductTemplateResources>();

            var allProducts = await this.productsClient.GetAllAsync(extractorParameters);

            List<ProductApiTemplateResource> apiProducts = new List<ProductApiTemplateResource>();
            if (!singleApiName.IsNullOrEmpty())
            {
                apiProducts = await this.productsClient.GetAllLinkedToApiAsync(singleApiName, extractorParameters);
            }

            foreach (var productTemplateResource in allProducts)
            {
                productTemplateResource.Name = $"[concat(parameters('{ParameterNames.ApimServiceName}'), '/{productTemplateResource.NewName}')]";
                productTemplateResource.ApiVersion = GlobalConstants.ApiVersion;

                // only extract the product if this is a full extraction, or in the case of a single api, if it is found in products associated with the api
                if (singleApiName == null || apiProducts.Any(p => p.Name.Equals(productTemplateResource.NewName)))
                {
                    this.logger.LogDebug("'{0}' product found", productTemplateResource.OriginalName);
                    productsTemplate.TypedResources.Products.Add(productTemplateResource);

                    await this.AddProductPolicyToTemplateResources(extractorParameters, productTemplateResource, productsTemplate.TypedResources, baseFilesGenerationDirectory);
                    await this.AddProductTagsToTemplateResources(extractorParameters, productTemplateResource, productsTemplate.TypedResources);
                    await this.AddGroupsLinkedToProductResources(extractorParameters, productTemplateResource, productsTemplate.TypedResources);
                }
            }

            return productsTemplate;
        }

        async Task AddProductTagsToTemplateResources(
            ExtractorParameters extractorParameters,
            ProductsTemplateResource productTemplateResource,
            ProductTemplateResources productTemplateResources)
        {
            try
            {
                var productTags = await this.tagClient.GetAllTagsLinkedToProductAsync(productTemplateResource.OriginalName, extractorParameters);

                if (productTags.IsNullOrEmpty())
                {
                    this.logger.LogWarning($"No tags found for product {productTemplateResource.OriginalName}");
                    return;
                }

                foreach (var productTag in productTags)
                {
                    string originalTagName = productTag.Name;
                    this.logger.LogDebug("'{0}' tag association found for {1} product", originalTagName, productTemplateResource.OriginalName);

                    productTag.Name = $"[concat(parameters('{ParameterNames.ApimServiceName}'), '/{productTemplateResource.NewName}/{originalTagName}')]";
                    productTag.Type = ResourceTypeConstants.ProductTag;
                    productTag.ApiVersion = GlobalConstants.ApiVersion;
                    productTag.Scale = null;
                    productTag.DependsOn = new string[] { $"[resourceId('Microsoft.ApiManagement/service/products', parameters('{ParameterNames.ApimServiceName}'), '{productTemplateResource.NewName}')]" };

                    productTemplateResources.Tags.Add(productTag);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Exception occured while product tags template generation");
                throw;
            }
        }

        async Task AddProductPolicyToTemplateResources(
            ExtractorParameters extractorParameters,
            ProductsTemplateResource productTemplateResource,
            ProductTemplateResources productTemplateResources,
            string filesGenerationDirectory)
        {
            var productResourceId = new string[] { $"[resourceId('Microsoft.ApiManagement/service/products', parameters('{ParameterNames.ApimServiceName}'), '{productTemplateResource.NewName}')]" };

            try
            {
                var productPolicyResource = await this.policyExtractor.GenerateProductPolicyTemplateAsync(
                    extractorParameters,
                    productTemplateResource,
                    productResourceId,
                    filesGenerationDirectory);

                if (productPolicyResource is not null)
                {
                    productTemplateResources.Policies.Add(productPolicyResource);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Exception occured while product policy template generation");
                throw;
            }
        }

        async Task AddGroupsLinkedToProductResources(
            ExtractorParameters extractorParameters,
            ProductsTemplateResource productTemplateResource,
            ProductTemplateResources productTemplateResources)
        {
            var productResourceId = new string[] { $"[resourceId('Microsoft.ApiManagement/service/products', parameters('{ParameterNames.ApimServiceName}'), '{productTemplateResource.NewName}')]" };

            if (SKUTypes.IsConsumption(extractorParameters.CurrentSKU))
            {
                this.logger.LogInformation("Skipping generation of groups resources attached to groups for consumption sku...");
                return ;
            }

            try
            {
                var groupsLinkedToProduct = await this.groupsClient.GetAllLinkedToProductAsync(productTemplateResource.OriginalName, extractorParameters);

                foreach (var productGroup in groupsLinkedToProduct)
                {
                    productGroup.Name = $"[concat(parameters('{ParameterNames.ApimServiceName}'), '/{productTemplateResource.NewName}/{productGroup.Name}')]";
                    productGroup.Type = ResourceTypeConstants.ProductGroup;
                    productGroup.ApiVersion = GlobalConstants.ApiVersion;
                    productGroup.DependsOn = productResourceId;
                    productTemplateResources.Groups.Add(productGroup);
                }
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Exception occured while adding group linked to product");
                throw;
            }
        }
    }
}
