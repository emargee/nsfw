# NSFW

(N!nt3ndo Sw!tch File Wizard)

---
## What is this?

This is a tool that allows you to verify the integrity of your NSP files, identify any problems and convert the to a standard deterministic format so that they can be accurately verified and preserved.

This tool wouldn't be possible without the amazing work and knowledge of the following people:

* LibHac - https://github.com/Thealexbarney/LibHac/ (@thealexbarney)
* DarkMatterCore - https://github.com/DarkMatterCore/ (@DarkMatterCore)

Also, a big thanks to the people that have helped me test this tool and provided feedback.

> [!CAUTION]
> This software is still in active development and should be considered experimental and liable to change regularly. Please use with caution and always keep a backup of your files.


> [!IMPORTANT]
> This software is designed to work with legal backup copies of your own games.
---
## Installation

1. Extract the latest release of `nsfw` (that matches your system architecture) to a directory of your choice.
1. (*Optional*) Extract latest `titledb.db` from `titledb.zip` to a directory called `titledb` in the same directory as the executable. 

## What is a "Standard NSP"?

See [Standard NSP](docs/standard_nsp.md) for more information.

## Validate (`v`)

```shell
USAGE:
    nsfw validate <NSP_FILE> [OPTIONS]

ARGUMENTS:
    <NSP_FILE>    Path to NSP file

OPTIONS:
                             DEFAULT
    -h, --help                                        Prints help information
    -v, --version                                     Prints version information
    -k, --keys <FILE>        ~/.switch/prod.keys      Path to NSW keys file
    -c, --cert <FILE>        ~/.switch/common.cert    Path to 0x700-byte long common certificate chain file
    -x, --extract                                     Extract NSP contents to output directory
    -s, --standard                                    Convert to Standardised NSP
    -r, --rename                                      Rename NSP to match TitleDB. No other actions performed
        --nspdir <DIR>       ./nsp                    Path to standardised NSP output directory
        --cdndir <DIR>       ./cdn                    Path to CDN output directory
    -d, --dryrun                                      Prints actions to be performed but does not execute any of them
        --titledb <FILE>     ./titledb/titledb.db     Path to titledb.db file
        --verify                                      Verify title against TitleDB
        --related-titles                              For DLC, print related titles (from TitleDb)
        --regional-titles                             Print regional title variations (from TitleDb)
        --updates                                     Print title update versions (from TitleDb)
        --nl                                          Do not include any languages in output filename
        --sl                                          Use short language codes in output filename
        --skip-hash                                   When re-naming files, skip NCA hash validation
        --quiet                                       Set output level to 'quiet'. Minimal display for details
        --full                                        Set output level to 'full'. Full break-down on NSP structure
        --force-hash                                  Force hash verification of bad NCA files (where header validation has
                                                      already failed)
        --extract-all                                 Extract all files from NSP (including loose files)
        --keep-deltas                                 When creating a standard NSP, include Delta Fragment files
        --overwrite                                   Overwrite any existing files
```

### Validate - Example Usage

Validate

`./nsfw v <PATH_TO_NSP_FILE>`

Validate - Full Details

`./nsfw v --full <PATH_TO_NSP_FILE>`

Validate - Minimal details

`./nsfw v --quiet <PATH_TO_NSP_FILE>`

Validate - Verify against TitleDB - Uses TitleDB to check title and metadata (if found).

`./nsfw v --verify <PATH_TO_NSP_FILE>`

Validate - Regional Titles - Uses TitleDB to print regional language variations of the title (if found).

`./nsfw v --verify --regional-titles <PATH_TO_NSP_FILE>`

Validate - Related Titles - If file is DLC, print other possible DLC titles (if found).

`./nsfw v --verify --related-titles <PATH_TO_NSP_FILE>`

Validate - Updates - Uses TitleDB to list updates for the title (if found).

`./nsfw v --updates --related-titles <PATH_TO_NSP_FILE>`

Validate - Force Hash - By default, if header validation fails, the NCA file will not be hashed, as the file is already invalid. This option will force the NCA file to be hashed regardless of header validity.

`./nsfw v --force-hash <PATH_TO_NSP_FILE>`

Batch verification is also supported (Output is set to `--quiet` by default):

`./nsfw v <PATH_TO_NSP_DIRECTORY>`

---

### Rename (`v -r`) - Example Usage

Validate and Rename - If validation passes, then original file is renamed.

`./nsfw v -r <PATH_TO_NSP_FILE>`

Validate and Rename - Dry Run - Will validate and print name but will not action any changes.

`./nsfw v -r -d <PATH_TO_NSP_FILE>`

Validate and Rename - No Languages - Will not include any languages in the output filename.

`./nsfw v -r --nl <PATH_TO_NSP_FILE>`

Validate and Rename - Short Languages - Will use short language codes in the output filename.

`./nsfw v -r --sl <PATH_TO_NSP_FILE>`

Validate and Rename - Skip Hash - Will skip hash validation of NCA files. Useful for files that have already been validated and just need to be renamed. This option will only work for renaming.

`./nsfw v -r --skip-hash <PATH_TO_NSP_FILE>`

---

### Extract (`v -x`) - Example Usage

Extract NSP file to No-Intro CDN format.

Validate and Extract - If validation passes, the original file is extracted to a directory defined by the `--cdndir` option.

`./nsfw v -x <PATH_TO_NSP_FILE>`

Validate and Extract - Dry Run - Will validate and print actions but will not execute them.

`./nsfw v -x -d <PATH_TO_NSP_FILE>`

Validate and Extract - Extract All - Will extract all files from the NSP (including loose files).

`./nsfw v -x --extract-all <PATH_TO_NSP_FILE>`

---

### Create "Standard NSP" (`v -s`) - Example Usage

Will re-create the NSP file in a standardised format, normalising ticket properties and and re-ordering the NCA files to match the order in the CNMT file.

> [!IMPORTANT]
> This will not fix any signature or validation problems with the NSP file. It will abort if any validation errors are found.

> [!WARNING]
> This process will **exclude** Delta Fragment files from the rebuilt NSP as they effect standardisation. To include these please use the `--keep-deltas` option.

Create "Standard NSP" - Will write the new NSP file to the directory defined by the `--nspdir` option.

`./nsfw v -s <PATH_TO_NSP_FILE>`

Create "Standard NSP" - Dry Run - Will validate and print actions but will not execute them.

`./nsfw v -s -d <PATH_TO_NSP_FILE>`

---

## Analyse Ticket (`t`)

```shell
DESCRIPTION:
Read & print ticket properties from Ticket file.

USAGE:
    nsfw ticket <TIK_FILE> [OPTIONS]

ARGUMENTS:
    <TIK_FILE>    Path to tik file

OPTIONS:
                         DEFAULT
    -h, --help                                    Prints help information
    -v, --version                                 Prints version information
    -c, --cert <FILE>    ~/.switch/common.cert    Path to 0x700-byte long common certificate chain file
```

---

## Analyse CNMT (`m`)

```shell
DESCRIPTION:
Reads & print properties from CNMT NCA file.

USAGE:
    nsfw cnmt <META_NCA_FILE> [OPTIONS]

ARGUMENTS:
    <META_NCA_FILE>    Path to CNMT NCA file

OPTIONS:
                         DEFAULT
    -h, --help                                  Prints help information
    -v, --version                               Prints version information
    -k, --keys <FILE>    ~/.switch/prod.keys    Path to NSW keys file
```
---

## Query TitleDB (`q`)

```shell
DESCRIPTION:
Query TitleDB for Title ID.

USAGE:
    nsfw query <TITLEID> [OPTIONS]

ARGUMENTS:
    <TITLEID>    Title ID to search for

OPTIONS:
                            DEFAULT
    -h, --help                                      Prints help information
    -v, --version                                   Prints version information
        --titledb <FILE>    ./titledb/titledb.db    Path to titledb.db file
```
---
## CDN2NSP (`c2n`)

Will convert a No-Intro CDN format folder into a "Standard NSP" file.

```shell
DESCRIPTION:
Deterministically recreates NSP files from extracted CDN data following nxdumptool NSP generation guidelines.

USAGE:
    nsfw cdn2nsp [OPTIONS]

OPTIONS:
                          DEFAULT
    -h, --help                                     Prints help information
    -v, --version                                  Prints version information
    -i, --cdndir <DIR>    ./cdn                    Path to directory with extracted CDN data (will be processed recursively)
    -k, --keys <FILE>     ~/.switch/prod.keys      Path to NSW keys file
    -c, --cert <FILE>     ~/.switch/common.cert    Path to 0x700-byte long common certificate chain file
    -o, --outdir <DIR>    ./out                    Path to output directory
    -s, --check-shas                               Check SHA256 of all files in CDN directory and compare with CNMT hashes
    -v, --verbose                                  Enable verbose output
    -d, --dryrun                                   Process files but do not generate NSP
```

### CDN2NSP - Example Usage

Convert CDN to NSP - Will convert all files in the `--cdndir` directory and write the new NSP file to the directory defined by the `--outdir` option.

`./nsfw c2n`

### CDN2NSP - Dry Run - Example Usage

Convert CDN to NSP - Dry Run - Will convert all files in the `--cdndir` directory and print actions but will not execute them.

`./nsfw c2n -d`

### CDN2NSP - Check SHA256 - Example Usage

Convert CDN to NSP - Check SHA256 - Will convert all files in the `--cdndir` directory and hash the SHA256 of each file against the CNMT hash.

`./nsfw c2n -s`

### CDN2NSP - Verbose - Example Usage

Prints additional information about the NSP file. Not as comprehensive as `validate`.

`./nsfw c2n -v`

# Roadmap / TODO

* [ ] Add support for verifying `*.xci` files
* [ ] Add support for outputting XML DAT format for No-Intro verification
* [ ] Add support for outputting validation information in JSON format
