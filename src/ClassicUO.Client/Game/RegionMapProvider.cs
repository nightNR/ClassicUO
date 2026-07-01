// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ClassicUO.Assets;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game
{
    /// <summary>
    /// Loads (from disk cache) or builds (on a background thread) the static
    /// reachability region map for a facet, and publishes it for the walk gate.
    /// The build reads the map/statics files through its OWN independent OS file
    /// handles (StaticMapPassability.Build), never the game thread's shared
    /// FileReader, so it is safe off the game thread. The finished map is
    /// published via a single volatile assignment.
    /// NOTE: the on-disk cache under Data/regioncache/ is keyed only by facet —
    /// clear that folder after a client/mul update so a stale map isn't reused.
    /// </summary>
    internal sealed class RegionMapProvider
    {
        private readonly UOFileManager _fm;
        private readonly string _cacheDir;
        private readonly object _lock = new object();
        private volatile RegionMap _current;
        private volatile int _wantedFacet = -1;
        private readonly HashSet<int> _buildingFacets = new();

        public RegionMapProvider(UOFileManager fm, string cacheDir)
        {
            _fm = fm;
            _cacheDir = cacheDir;
        }

        public RegionMap Current => _current;

        public void EnsureFor(int facet)
        {
            _wantedFacet = facet;

            if (UltimaLive.UltimaLiveActive)
            {
                return; // live-edited map: don't trust a static build
            }

            if (_current != null && _current.Facet == facet)
            {
                return;
            }

            lock (_lock)
            {
                if (_buildingFacets.Contains(facet) || (_current != null && _current.Facet == facet))
                {
                    return;
                }

                if (TryLoadCache(facet, out RegionMap cached))
                {
                    _current = cached;
                    return;
                }

                // No synchronous cache hit and the current map (if any) is for a
                // different facet: null it out before kicking the background build so
                // the walk gate stays optimism-safe instead of gating against a stale
                // facet's map for the whole async build.
                if (_current != null && _current.Facet != facet)
                {
                    _current = null;
                }

                _buildingFacets.Add(facet);
            }

            Task.Run(() => BuildAndPublish(facet));
        }

        private void BuildAndPublish(int facet)
        {
            try
            {
                bool[] passable = StaticMapPassability.Build(_fm, facet);
                int w = StaticMapPassability.Width(_fm, facet);
                int h = StaticMapPassability.Height(_fm, facet);
                int[] ids = RegionMapBuilder.Build(w, h, (x, y) => passable[x * h + y]);
                var map = new RegionMap(facet, w, h, ids);

                if (facet == _wantedFacet)
                {
                    _current = map; // publish (volatile) only if still the wanted facet
                }
                TrySaveCache(map); // caching a correctly-built map for a facet we left is fine
            }
            catch (System.Exception ex)
            {
                Log.Warn($"RegionMapProvider build failed for facet {facet}: {ex.Message}");
            }
            finally
            {
                lock (_lock)
                {
                    _buildingFacets.Remove(facet);
                }
            }
        }

        private string PathFor(int facet) => Path.Combine(_cacheDir, $"region_{facet}.bin");

        private bool TryLoadCache(int facet, out RegionMap map)
        {
            map = null;
            try
            {
                string path = PathFor(facet);
                if (!File.Exists(path))
                {
                    return false;
                }
                using var fs = File.OpenRead(path);
                return RegionMapCache.TryRead(fs, facet, out map);
            }
            catch
            {
                map = null;
                return false;
            }
        }

        private void TrySaveCache(RegionMap map)
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                using var fs = File.Create(PathFor(map.Facet));
                RegionMapCache.Write(fs, map);
            }
            catch (System.Exception ex)
            {
                Log.Warn($"RegionMapProvider cache write failed: {ex.Message}");
            }
        }
    }
}
