import { BaseDirectory as UpstreamDirectory } from './upstream/path.js';
export { appConfigDir, appDataDir, appLocalDataDir, appCacheDir, appLogDir, audioDir, cacheDir, configDir, dataDir, desktopDir, documentDir, downloadDir, executableDir, fontDir, homeDir, localDataDir, pictureDir, publicDir, resourceDir, resolveResource, runtimeDir, templateDir, videoDir, sep, delimiter, resolve, normalize, join, dirname, extname, basename, isAbsolute, tempDir } from './upstream/path.js';
/** Explicit desktop directory list; upstream additions are reviewed separately. */
export declare const BaseDirectory: Readonly<{
    Audio: UpstreamDirectory.Audio;
    Cache: UpstreamDirectory.Cache;
    Config: UpstreamDirectory.Config;
    Data: UpstreamDirectory.Data;
    LocalData: UpstreamDirectory.LocalData;
    Document: UpstreamDirectory.Document;
    Download: UpstreamDirectory.Download;
    Picture: UpstreamDirectory.Picture;
    Public: UpstreamDirectory.Public;
    Video: UpstreamDirectory.Video;
    Resource: UpstreamDirectory.Resource;
    Temp: UpstreamDirectory.Temp;
    AppConfig: UpstreamDirectory.AppConfig;
    AppData: UpstreamDirectory.AppData;
    AppLocalData: UpstreamDirectory.AppLocalData;
    AppCache: UpstreamDirectory.AppCache;
    AppLog: UpstreamDirectory.AppLog;
    Desktop: UpstreamDirectory.Desktop;
    Executable: UpstreamDirectory.Executable;
    Font: UpstreamDirectory.Font;
    Home: UpstreamDirectory.Home;
    Runtime: UpstreamDirectory.Runtime;
    Template: UpstreamDirectory.Template;
}>;
export type BaseDirectory = typeof BaseDirectory[keyof typeof BaseDirectory];
