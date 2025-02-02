﻿// --------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation. All rights reserved.
//  Licensed under the MIT License.
// --------------------------------------------------------------------------

using System;

namespace Microsoft.Azure.Management.ApiManagement.ArmTemplates.Common.Templates.ApiOperations
{
    public class ApiOperationHeader
    {
        public string DefaultValue { get; set; }

        public string Description { get; set; }

        public string Name { get; set; }

        public string Type { get; set; }

        public string[] Values { get; set; } = Array.Empty<string>();

        public bool Required { get; set; }
    }
}
