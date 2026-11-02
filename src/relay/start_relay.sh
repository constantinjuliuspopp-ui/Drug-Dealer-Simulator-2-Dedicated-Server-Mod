#!/bin/sh
# Startet DDS2Relay mit relay.cfg aus diesem Ordner (Linux).
# Benutzung:  sh start_relay.sh     (weitere Parameter werden durchgereicht)
cd "$(dirname "$0")" || exit 1
chmod +x ./DDS2Relay 2>/dev/null
[ -f relay.cfg ] || cp relay.cfg.example relay.cfg
exec ./DDS2Relay --config relay.cfg "$@"
