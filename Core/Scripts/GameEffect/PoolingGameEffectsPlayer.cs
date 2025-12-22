using System.Collections.Generic;
using UnityEngine;

namespace MultiplayerARPG
{
    public class PoolingGameEffectsPlayer : MonoBehaviour, IPoolDescriptorCollection
    {
        public GameEffectPoolContainer[] poolingGameEffects;

        // Cached to avoid GC allocations
        private IPoolDescriptor[] _cachedDescriptors;

        public IEnumerable<IPoolDescriptor> PoolDescriptors
        {
            get
            {
                if (_cachedDescriptors == null)
                {
                    if (poolingGameEffects == null || poolingGameEffects.Length == 0)
                    {
                        _cachedDescriptors = System.Array.Empty<IPoolDescriptor>();
                    }
                    else
                    {
                        _cachedDescriptors = new IPoolDescriptor[poolingGameEffects.Length];
                        for (int i = 0; i < poolingGameEffects.Length; ++i)
                        {
                            _cachedDescriptors[i] = poolingGameEffects[i].prefab;
                        }
                    }
                }

                return _cachedDescriptors;
            }
        }

        public void PlayRandomEffect()
        {
            if (Application.isBatchMode)
                return;

            if (poolingGameEffects != null && poolingGameEffects.Length > 0)
            {
                poolingGameEffects[Random.Range(0, poolingGameEffects.Length)].GetInstance();
            }
        }

#if UNITY_EDITOR
        // Ensure cache stays valid if modified in inspector
        private void OnValidate()
        {
            _cachedDescriptors = null;
        }
#endif
    }
}
