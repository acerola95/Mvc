// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.AspNet.Mvc;
using Microsoft.AspNet.Http;
using System.IO;

namespace ModelBindingWebSite.Controllers
{
    public class FormCollectionController : Controller
    {
        public IList<string> ReturnValuesAsList(IFormCollection form)
        {
            if (!ModelState.IsValid || form == null)
            {
                return null;
            }

            var valuesList = new List<string>();

            valuesList.Add(form["field1"]);
            valuesList.Add(form["field2"]);

            return valuesList;
        }

        public ActionResult ReturnFileContent(IFormCollection form)
        {
            if (!ModelState.IsValid || form == null)
            {
                return null;
            }

            var file = form.Files.GetFile("File");
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                var fileContent = reader.ReadToEnd();

                return Content(fileContent);
            }
        }
    }
}
