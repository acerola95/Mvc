// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNet.FileProviders;
using Microsoft.Framework.Cache.Memory;
using Microsoft.Framework.OptionsModel;

namespace Microsoft.AspNet.Mvc.Razor
{
    /// <summary>
    /// Caches the result of runtime compilation of Razor files for the duration of the app lifetime.
    /// </summary>
    public class CompilerCache : ICompilerCache
    {
        private readonly IFileProvider _fileProvider;
        private readonly IMemoryCache _cache;

        /// <summary>
        /// Initializes a new instance of <see cref="CompilerCache"/> populated with precompiled views
        /// discovered using <paramref name="provider"/>.
        /// </summary>
        /// <param name="provider">
        /// An <see cref="IAssemblyProvider"/> representing the assemblies
        /// used to search for pre-compiled views.
        /// </param>
        /// <param name="optionsAccessor">An accessor to the <see cref="RazorViewEngineOptions"/>.</param>
        public CompilerCache(IAssemblyProvider provider,
                             IOptions<RazorViewEngineOptions> optionsAccessor)
            : this(GetFileInfos(provider.CandidateAssemblies), optionsAccessor.Options.FileProvider)
        {
        }

        // Internal for unit testing
        internal CompilerCache(IEnumerable<RazorFileInfoCollection> razorFileInfoCollection,
                               IFileProvider fileProvider)
        {
            _fileProvider = fileProvider;
            _cache = new MemoryCache(new MemoryCacheOptions { ListenForMemoryPressure = false });
            var cacheEntries = new List<CompilerCacheEntry>();
            foreach (var viewCollection in razorFileInfoCollection)
            {
                var containingAssembly = viewCollection.GetType().GetTypeInfo().Assembly;
                foreach (var fileInfo in viewCollection.FileInfos)
                {
                    var viewType = containingAssembly.GetType(fileInfo.FullTypeName);
                    var cacheEntry = new CompilerCacheEntry(fileInfo, viewType);

                    // There shouldn't be any duplicates and if there are any the first will win.
                    // If the result doesn't match the one on disk its going to recompile anyways.
                    _cache.Set(NormalizePath(fileInfo.RelativePath), cacheEntry, PopulateCacheSetContext);

                    cacheEntries.Add(cacheEntry);
                }
            }

            // Set up ViewStarts
            foreach (var entry in cacheEntries)
            {
                var viewStartLocations = ViewStartUtility.GetViewStartLocations(entry.RelativePath);
                foreach (var location in viewStartLocations)
                {
                    var viewStartEntry = _cache.Get<CompilerCacheEntry>(location);
                    if (viewStartEntry != null)
                    {
                        // Add the the composite _ViewStart entry as a dependency.
                        entry.AssociatedViewStartEntry = viewStartEntry;
                        break;
                    }
                }
            }
        }

        /// <inheritdoc />
        public CompilerCacheResult GetOrAdd([NotNull] string relativePath,
                                            [NotNull] Func<RelativeFileInfo, CompilationResult> compile)
        {
            var result = GetOrAddCore(relativePath, compile);
            if (result == null)
            {
                return CompilerCacheResult.FileNotFound;
            }

            return new CompilerCacheResult(result.CompilationResult);
        }

        private GetOrAddResult GetOrAddCore(string relativePath,
                                            Func<RelativeFileInfo, CompilationResult> compile)
        {
            var normalizedPath = NormalizePath(relativePath);
            var cacheEntry = _cache.Get<CompilerCacheEntry>(normalizedPath);
            if (cacheEntry == null)
            {
                var fileInfo = _fileProvider.GetFileInfo(relativePath);
                if (!fileInfo.Exists)
                {
                    return null;
                }

                var relativeFileInfo = new RelativeFileInfo(fileInfo, relativePath);
                return OnCacheMiss(relativeFileInfo, normalizedPath, compile);
            }
            else if (cacheEntry.IsPreCompiled && !cacheEntry.IsValidatedPreCompiled)
            {
                // For precompiled views, the first time the entry is read, we need to ensure that no changes were made
                // either to the file associated with this entry, or any _ViewStart associated with it between the time
                // the View was precompiled and the time EnsureInitialized was called. For later iterations, we can
                // rely on expiration triggers ensuring the validity of the entry.

                var fileInfo = _fileProvider.GetFileInfo(relativePath);
                if (!fileInfo.Exists)
                {
                    return null;
                }

                var relativeFileInfo = new RelativeFileInfo(fileInfo, relativePath);
                if (cacheEntry.Length != fileInfo.Length)
                {
                    // Recompile if the file lengths differ
                    return OnCacheMiss(relativeFileInfo, normalizedPath, compile);
                }

                if (AssociatedViewStartsChanged(cacheEntry, compile))
                {
                    // Recompile if the view starts have changed since the entry was created.
                    return OnCacheMiss(relativeFileInfo, normalizedPath, compile);
                }

                if (cacheEntry.LastModified == fileInfo.LastModified)
                {
                    // Assigning to IsValidatedPreCompiled is an atomic operation and will result in a safe race
                    // if it is being concurrently updated and read.
                    cacheEntry.IsValidatedPreCompiled = true;
                    return new GetOrAddResult
                    {
                        CompilationResult = CompilationResult.Successful(cacheEntry.CompiledType),
                        CompilerCacheEntry = cacheEntry
                    };
                }

                // Timestamp doesn't match but it might be because of deployment, compare the hash.
                if (cacheEntry.IsPreCompiled &&
                    string.Equals(cacheEntry.Hash,
                                  RazorFileHash.GetHash(fileInfo, cacheEntry.HashAlgorithmVersion),
                                  StringComparison.Ordinal))
                {
                    // Cache hit, but we need to update the entry.
                    // Assigning to LastModified and IsValidatedPreCompiled are atomic operations and will result in safe race
                    // if the entry is being concurrently read or updated.
                    cacheEntry.LastModified = fileInfo.LastModified;
                    cacheEntry.IsValidatedPreCompiled = true;
                    return new GetOrAddResult
                    {
                        CompilationResult = CompilationResult.Successful(cacheEntry.CompiledType),
                        CompilerCacheEntry = cacheEntry
                    };
                }

                // it's not a match, recompile
                return OnCacheMiss(relativeFileInfo, normalizedPath, compile);
            }

            return new GetOrAddResult
            {
                CompilationResult = CompilationResult.Successful(cacheEntry.CompiledType),
                CompilerCacheEntry = cacheEntry
            };
        }

        private GetOrAddResult OnCacheMiss(RelativeFileInfo file,
                                           string normalizedPath,
                                           Func<RelativeFileInfo, CompilationResult> compile)
        {
            var compilationResult = compile(file);

            // Concurrent addition to MemoryCache with the same key result in safe race.
            var cacheEntry = _cache.Set(normalizedPath,
                                        new CompilerCacheEntry(file, compilationResult.CompiledType),
                                        PopulateCacheSetContext);
            return new GetOrAddResult
            {
                CompilationResult = compilationResult,
                CompilerCacheEntry = cacheEntry
            };
        }

        private CompilerCacheEntry PopulateCacheSetContext(ICacheSetContext cacheSetContext)
        {
            var entry = (CompilerCacheEntry)cacheSetContext.State;
            cacheSetContext.AddExpirationTrigger(_fileProvider.Watch(entry.RelativePath));

            var viewStartLocations = ViewStartUtility.GetViewStartLocations(cacheSetContext.Key);
            foreach (var location in viewStartLocations)
            {
                cacheSetContext.AddExpirationTrigger(_fileProvider.Watch(location));
            }

            return entry;
        }

        private bool AssociatedViewStartsChanged(CompilerCacheEntry entry,
                                                 Func<RelativeFileInfo, CompilationResult> compile)
        {
            var viewStartEntry = GetCompositeViewStartEntry(entry.RelativePath, compile);
            return entry.AssociatedViewStartEntry != viewStartEntry;
        }

        // Returns the entry for the nearest _ViewStart that the file inherits directives from. Since _ViewStart
        // entries are affected by other _ViewStart entries that are in the path hierarchy, the returned value
        // represents the composite result of performing a cache check on individual _ViewStart entries.
        private CompilerCacheEntry GetCompositeViewStartEntry(string relativePath,
                                                              Func<RelativeFileInfo, CompilationResult> compile)
        {
            var viewStartLocations = ViewStartUtility.GetViewStartLocations(relativePath);
            foreach (var viewStartLocation in viewStartLocations)
            {
                var getOrAddResult = GetOrAddCore(viewStartLocation, compile);
                if (getOrAddResult != null)
                {
                    // This is the nearest _ViewStart that exists on disk.
                    return getOrAddResult.CompilerCacheEntry;
                }
            }

            // No _ViewStarts discovered.
            return null;
        }

        private static string NormalizePath(string path)
        {
            // We need to allow for scenarios where the application was precompiled on a machine with forward slashes
            // but is being run in one with backslashes (or vice versa). To this effect, we'll normalize paths to
            // use backslashes for lookups and storage in the dictionary.
            path = path.Replace('/', '\\');
            path = path.TrimStart('\\');

            return path;
        }

        internal static IEnumerable<RazorFileInfoCollection>
                    GetFileInfos(IEnumerable<Assembly> assemblies)
        {
            return assemblies.SelectMany(a => a.ExportedTypes)
                    .Where(Match)
                    .Select(c => (RazorFileInfoCollection)Activator.CreateInstance(c));
        }

        private static bool Match(Type t)
        {
            var inAssemblyType = typeof(RazorFileInfoCollection);
            if (inAssemblyType.IsAssignableFrom(t))
            {
                var hasParameterlessConstructor = t.GetConstructor(Type.EmptyTypes) != null;

                return hasParameterlessConstructor
                    && !t.GetTypeInfo().IsAbstract
                    && !t.GetTypeInfo().ContainsGenericParameters;
            }

            return false;
        }


        private class GetOrAddResult
        {
            public CompilerCacheEntry CompilerCacheEntry { get; set; }

            public CompilationResult CompilationResult { get; set; }
        }
    }
}
