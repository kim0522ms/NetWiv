#!/usr/bin/env bash
set -euo pipefail

stop_children() {
  kill "$@" 2>/dev/null || true
  local child forced=false running
  for ((cleanup_attempt=0; cleanup_attempt<30; cleanup_attempt++)); do
    running=false
    for child in "$@"; do
      if jobs -pr | grep -qx "$child"; then running=true; fi
    done
    [[ $running == false ]] && break
    sleep 0.1
  done
  for child in "$@"; do
    # Only kill a still-running child owned by this shell, never a reused PID.
    if jobs -pr | grep -qx "$child"; then
      echo "Desktop helper $child required forced termination" >&2
      kill -KILL "$child" 2>/dev/null || true
      forced=true
    fi
    wait "$child" 2>/dev/null || true
  done
  [[ $forced == false ]]
}

stop_panel() {
  local child=$1
  # The panel owns plugin processes. Ask its session-local service to quit
  # before sending SIGTERM, so plugins can release their GTK/DBus resources.
  if jobs -pr | grep -qx "$child"; then
    GDK_BACKEND=x11 timeout 2s xfce4-panel --quit >&2 || true
    for ((panel_attempt=0; panel_attempt<20; panel_attempt++)); do
      if ! jobs -pr | grep -qx "$child"; then break; fi
      sleep 0.1
    done
  fi
  stop_children "$child"
}

remove_config() {
  local directory=$1 attempt
  # dbus-run-session can return before its activated xfconfd finishes flushing
  # settings. Retry only deletion of our private directory while it winds down.
  for ((attempt=0; attempt<30; attempt++)); do
    if rm -rf -- "$directory"; then return 0; fi
    sleep 0.1
  done
  return 1
}

# A dedicated Xfce panel provides the StatusNotifierWatcher required by Ayatana.
# Keep its configuration and DBus session separate from the user's desktop.
if [[ "${1:-}" == "--session" ]]; then
  shift
  executable=$1
  shift
  GDK_BACKEND=x11 xfwm4 --compositor=off >&2 &
  wm=$!
  GDK_BACKEND=x11 xfce4-panel --disable-wm-check >&2 &
  panel=$!
  # Keep the window manager alive while panel windows and tray widgets close.
  trap 'result=$?; if ! stop_panel "$panel"; then result=125; fi; if ! stop_children "$wm"; then result=125; fi; exit "$result"' EXIT
  ready=false
  for ((attempt=0; attempt<50; attempt++)); do
    if timeout 2s xprop -root _NET_SUPPORTING_WM_CHECK | grep -Eq 'window id # 0x[[:xdigit:]]+' &&
      timeout 2s gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
      --method org.freedesktop.DBus.NameHasOwner org.kde.StatusNotifierWatcher | grep -q true; then
      ready=true; break
    fi
    sleep 0.1
  done
  if [[ $ready != true ]]; then echo 'Window manager or StatusNotifierWatcher did not become ready' >&2; exit 1; fi
  # Only the nested desktop uses the temporary Xfce configuration. Reference
  # and candidate apps must resolve the same user directories across runs.
  export XDG_CONFIG_HOME="$NETWIV_DESKTOP_APP_CONFIG_HOME"
  GDK_BACKEND=x11 NETWIV_EXPECT_BACKEND=x11 G_DEBUG=fatal-criticals WEBKIT_DISABLE_COMPOSITING_MODE=1 "$executable" "$@"
  exit $?
fi

if [[ $# -lt 2 || ! "$2" =~ ^(x11|wayland)$ ]]; then
  echo 'usage: run-linux-smoke-desktop.sh /absolute/executable x11|wayland [arguments...]' >&2; exit 2
fi
executable=$(realpath "$1")
backend=$2
shift 2

if [[ $backend == wayland ]]; then
  # Use the logged-in desktop, including its compositor, session bus and tray
  # host. DISPLAY may still be needed by optional XWayland-backed plugins.
  if [[ -z ${WAYLAND_DISPLAY:-} ]]; then
    echo 'Wayland validation requires WAYLAND_DISPLAY from an existing desktop session' >&2; exit 1
  fi
  socket=$WAYLAND_DISPLAY
  if [[ $socket != /* ]]; then
    if [[ -z ${XDG_RUNTIME_DIR:-} ]]; then
      echo 'A relative WAYLAND_DISPLAY requires XDG_RUNTIME_DIR' >&2; exit 1
    fi
    socket=$XDG_RUNTIME_DIR/$socket
  fi
  if [[ ! -S $socket ]]; then
    echo 'The existing Wayland display socket is unavailable' >&2; exit 1
  fi
  if [[ -z ${DBUS_SESSION_BUS_ADDRESS:-} ]]; then
    echo 'Wayland validation requires the existing DBUS_SESSION_BUS_ADDRESS' >&2; exit 1
  fi
  for tool in timeout gdbus; do
    command -v "$tool" >/dev/null || { echo "Missing native GUI test dependency: $tool" >&2; exit 1; }
  done
  if ! timeout 2s gdbus call --session --dest org.freedesktop.DBus --object-path /org/freedesktop/DBus \
    --method org.freedesktop.DBus.GetId >/dev/null 2>&1; then
    echo 'The existing D-Bus session is unavailable' >&2; exit 1
  fi
  export GDK_BACKEND=wayland NETWIV_EXPECT_BACKEND=wayland
  exec "$executable" "$@"
fi

export NETWIV_EXPECT_BACKEND=x11
for tool in Xephyr xfwm4 xfce4-panel dbus-run-session xdpyinfo xprop gdbus; do
  command -v "$tool" >/dev/null || { echo "Missing native GUI test dependency: $tool" >&2; exit 1; }
done
display=${NETWIV_TEST_DISPLAY:-:91}
if DISPLAY="$display" timeout 2s xdpyinfo >/dev/null 2>&1; then echo "Test display $display is already in use" >&2; exit 1; fi
config=$(mktemp -d)
export NETWIV_DESKTOP_APP_CONFIG_HOME="${XDG_CONFIG_HOME:-$HOME/.config}"
export XDG_CONFIG_HOME="$config"
mkdir -p "$config/xfce4/xfconf/xfce-perchannel-xml"
cat >"$config/xfce4/xfconf/xfce-perchannel-xml/xfce4-panel.xml" <<'XML'
<?xml version="1.0" encoding="UTF-8"?>
<channel name="xfce4-panel" version="1.0">
 <property name="configver" type="int" value="2"/>
 <property name="panels" type="array"><value type="int" value="1"/>
  <property name="panel-1" type="empty">
   <property name="position" type="string" value="p=6;x=640;y=16"/>
   <property name="length" type="uint" value="100"/>
   <property name="size" type="uint" value="32"/>
   <property name="position-locked" type="bool" value="true"/>
   <property name="plugin-ids" type="array"><value type="int" value="1"/></property>
  </property>
 </property>
 <property name="plugins" type="empty"><property name="plugin-1" type="string" value="systray"/></property>
</channel>
XML
Xephyr "$display" -screen 1280x800 -br -nolisten tcp >&2 &
xephyr=$!
trap 'result=$?; if ! stop_children "$xephyr"; then result=125; fi; if ! remove_config "$config" && [[ $result == 0 ]]; then result=125; fi; exit "$result"' EXIT
export DISPLAY="$display"
for ((attempt=0; attempt<50; attempt++)); do
  if timeout 2s xdpyinfo >/dev/null 2>&1; then break; fi
  sleep 0.1
done
timeout 2s xdpyinfo >/dev/null 2>&1 || { echo 'Xephyr did not start' >&2; exit 1; }
dbus-run-session -- bash "$0" --session "$executable" "$@"
