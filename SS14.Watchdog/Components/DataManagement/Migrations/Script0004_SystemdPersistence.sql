-- When using the "Systemd" process manager, the unit name of the service process.
ALTER TABLE ServerInstance ADD COLUMN PersistedSystemdUnit TEXT;

