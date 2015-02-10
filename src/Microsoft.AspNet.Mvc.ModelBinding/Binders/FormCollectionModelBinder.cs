// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Core.Collections;

namespace Microsoft.AspNet.Mvc.ModelBinding
{
    /// <summary>
    /// Modelbinder to bind form values to <see cref="IFormCollection"/>.
    /// </summary>
    public class FormCollectionModelBinder : IModelBinder
    {
        /// <inheritdoc />
        public async Task<bool> BindModelAsync([NotNull] ModelBindingContext bindingContext)
        {
            if (!bindingContext.ModelType.GetTypeInfo().IsAssignableFrom(
                    typeof(FormCollection).GetTypeInfo()))
            {
                return false;
            }

            var request = bindingContext.OperationBindingContext.HttpContext.Request;
            if (request.HasFormContentType)
            {
                bindingContext.Model = await request.ReadFormAsync();
            }
            else
            {
                bindingContext.Model = new FormCollection(new Dictionary<string, string[]>());
            }

            return true;
        }
    }
}