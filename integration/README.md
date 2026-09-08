# DovahLink integration tests

This directory is reserved for tests and scenarios that exercise boundaries between areas, per
`ai/context/common.md`'s "Repository boundaries". Its previous contents -- an independent .NET
validation client and end-to-end scenarios driving a real `dovahlink_bridge_harness` process --
were removed in `roadmap/03a-host-adapter-production-migration.md`'s 3A.2 ("Legacy Bridge
Removal") once `bridge/` itself was deleted; they had no way to run against anything once their
only target was gone.

No integration coverage currently lives here. Future Host/Adapter integration or end-to-end
coverage belongs in this directory when it is added.
