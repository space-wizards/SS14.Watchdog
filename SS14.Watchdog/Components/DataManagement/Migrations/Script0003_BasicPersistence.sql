-- Contains the watchdog token given to the active game server.
ALTER TABLE ServerInstance ADD COLUMN PersistedToken TEXT;

-- When using the "Basic" process manager, the PID of the game server process.
ALTER TABLE ServerInstance ADD COLUMN PersistedPid INTEGER;
