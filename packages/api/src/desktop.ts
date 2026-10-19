const mobileMethods = new Set<PropertyKey>(['activityName', 'sceneIdentifier']);
const mobileOptions = [
  'activityName', 'createdByActivityName', 'requestedBySceneIdentifier',
  'disableInputAccessoryView'
];
const handles = new WeakMap<object, object>();

/** Keep the upstream receiver and identity while exposing the desktop handle. */
export function desktopHandle<T extends object>(handle: T): T {
  const cached = handles.get(handle);
  if (cached) return cached as T;
  const proxy = new Proxy(handle, {
    get(target, key) {
      if (mobileMethods.has(key)) return undefined;
      const value = Reflect.get(target, key, target);
      if (key === 'window' && value) return desktopHandle(value);
      return typeof value === 'function' && key !== 'constructor'
        ? value.bind(target) : value;
    },
    has: (target, key) => !mobileMethods.has(key) && Reflect.has(target, key)
  });
  handles.set(handle, proxy);
  handles.set(proxy, proxy);
  return proxy;
}

export function desktopConstructor<T extends new (...args: any[]) => object>(
  constructor: T, optionIndex: number, factories: Record<string, unknown>
): unknown {
  return new Proxy(constructor, {
    construct(target, args, newTarget) {
      const options = args[optionIndex];
      for (const name of mobileOptions) {
        if (options && name in options) {
          throw new TypeError(`@netwiv/api does not support the mobile option '${name}'.`);
        }
      }
      return desktopHandle(Reflect.construct(target, args, newTarget));
    },
    get: (target, key) => typeof key === 'string' && key in factories
      ? factories[key] : Reflect.get(target, key)
  });
}
