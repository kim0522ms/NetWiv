/** Keep the upstream receiver and identity while exposing the desktop handle. */
export declare function desktopHandle<T extends object>(handle: T): T;
export declare function desktopConstructor<T extends new (...args: any[]) => object>(constructor: T, optionIndex: number, factories: Record<string, unknown>): unknown;
