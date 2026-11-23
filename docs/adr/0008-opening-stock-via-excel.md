# ADR-0008 Opening stock via Excel import

Status: Accepted

## Decision
The book import template includes an OpeningStock column. Import creates/updates books and writes OpeningStock movements for new stock; re-importing a book with OpeningStock adds an Adjustment movement only when the value differs and the user ticks 'overwrite stock'.
