// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNet.Http;

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
            if (!typeof(IFormCollection).GetTypeInfo().IsAssignableFrom(
                    bindingContext.ModelType.GetTypeInfo()))
            {
                return false;
            }

            var request = bindingContext.OperationBindingContext.HttpContext.Request;
            if (request.HasFormContentType)
            {
                bindingContext.Model = await request.ReadFormAsync();
            }

            return true;
        }
    }
}