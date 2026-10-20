const mobileMethods = new Set(['activityName', 'sceneIdentifier']);
const mobileOptions = [
    'activityName', 'createdByActivityName', 'requestedBySceneIdentifier',
    'disableInputAccessoryView'
];
const handles = new WeakMap();
/** Keep the upstream receiver and identity while exposing the desktop handle. */
export function desktopHandle(handle) {
    const cached = handles.get(handle);
    if (cached)
        return cached;
    const proxy = new Proxy(handle, {
        get(target, key) {
            if (mobileMethods.has(key))
                return undefined;
            const value = Reflect.get(target, key, target);
            if (key === 'window' && value)
                return desktopHandle(value);
            return typeof value === 'function' && key !== 'constructor'
                ? value.bind(target) : value;
        },
        has: (target, key) => !mobileMethods.has(key) && Reflect.has(target, key)
    });
    handles.set(handle, proxy);
    handles.set(proxy, proxy);
    return proxy;
}
export function desktopConstructor(constructor, optionIndex, factories) {
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
