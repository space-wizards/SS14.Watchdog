-- Stores information on all servers managed by the watchdog.
CREATE TABLE ServerInstance(
    -- The identifier name configured for the instance.
    Key TEXT NOT NULL PRIMARY KEY,

    -- The revision information (which version is currently running).
    Revision TEXT NULL
);


