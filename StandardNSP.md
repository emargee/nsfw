Version: *0.2*

# Standard NSP
## What's the big issue ?
Currently, NSPs are *non-deterministic*, meaning that 2 or more people could dump the same NSP (Game/Update/DLC etc) and end up with different results.
This is not great for preservation as it makes it harder to verify a known dump and we end up with situations like this:

![image](https://github.com/emargee/nsfw/assets/221695/90f2ad33-12a2-4126-98e8-cb4a4c3f0ba1)

Where the same item dumped by different people gets different results and one is not objectively "better" than the other, so we DAT all of them.
(**Note**: Yes, of course there will be times when there are actual differences but we can get into that later on!)

This proposal looks at how we can try and bring NSPs into a more controlled format, with certain rules that assure deterministic generation, while making sure we preserve the data correctly. It will also allow us to process older dumped files and (maybe!) correct issues that have been fixed in later versions of dumping tools.

### What is an NSP ?
If you think of an NSP like a file-system folder, if you opened it up it might contain:
1. One or more `.nca` files (the main data types)
2. One `.cnmt.nca` - Think of this like an index of the NCAs expected in the NSP.
3. `.tik` + `.cert` files - Optional, only needed for when the NSP uses titlekey encryption
4. The NSP format also supports "loose" files (ie not indexed by `*.cnmt.nca`).

An example NSP when expanded could look like this:
```
PFS0:
 ├── 317e048a8e3a4c92f072b33378106f37.cnmt.nca
 ├── 317e048a8e3a4c92f072b33378106f37.cnmt.xml
 ├── 671d172e7993ee033d1be25ee76378e3.nca
 ├── ac89f1af8740787d87d64fe9e5dc66e7.nca
 ├── a0f6f6c8ac3b97885d43cabc4473925f.nca
 ├── 01040719881839ccd0a49b36bbc11934.nca
 ├── 671d172e7993ee033d1be25ee76378e3.programinfo.xml
 ├── a0f6f6c8ac3b97885d43cabc4473925f.nx.AmericanEnglish.jpg
 ├── a0f6f6c8ac3b97885d43cabc4473925f.nacp.xml
 ├── 01040719881839ccd0a49b36bbc11934.legalinfo.xml
 ├── ac89f1af8740787d87d64fe9e5dc66e7.htmldocument.xml
 ├── 010025400aece0000000000000000005.tik
 └── 010025400aece0000000000000000005.cert
```
The metadata NCA (`*.cnmt.nca`) index looks like this:

```
Metadata Content:
 ├── 671d172e7993ee033d1be25ee76378e3.nca [Program]
 ├── a0f6f6c8ac3b97885d43cabc4473925f.nca [Control]
 ├── ac89f1af8740787d87d64fe9e5dc66e7.nca [HtmlDocument]
 └── 01040719881839ccd0a49b36bbc11934.nca [LegalInformation]
```

### Issues with this
When we hash an NSP there are various factors that will effect the outcome:

1. An NCA may have an invalid header - This is a header signed by "Big N", if it does not validate it means it has been tampered with or is broken/badly dumped. This does not effect the running of the game on a hacked NSW but is not useful for preservation. So there are times where a bad NSP is generated but not noticed/cared-about as it still runs.
1. An NCA may have an invalid hash - Damaged or incomplete data section.
1. An NCA may have been truncated/trimmed to save space.
1. An NSP can sometimes contain loose files such as JPG + XML added by the dumping tool - sometimes not.
   From our example:
   ```
    ├── 317e048a8e3a4c92f072b33378106f37.cnmt.nca
    ├── 317e048a8e3a4c92f072b33378106f37.cnmt.xml
   ```
   This XML file is a dump of the cnmt metadata - this is not an original file but was created by the dump tool. It has no use for preservation (its duplicate data from the `cnmt.nca`)
   It might be useful but means, if included, would result in a different hash of the NSP compared to a version that excluded it.

1. Files can be added in a random order so:
   ```
    ├── 01.nca
    └── 02.nca
   ```
   will hash differently than:
   ```
    ├── 02.nca
    └── 01.nca
   ```
1. The Ticket (`.tik`) may have property differences or contain personal information (see below)
1. The certificate (`.cert`) may not be the `common certificate` (see below)

### Delta files - Its a bit complicated.
In an "Update" NSP you will often find "Delta Fragment" type NCA files. These are delta-patch files that can be used to "patch" an existing game (the idea being it will be a quicker operation as you are patching an existing file with, what might be, a small change).

For example, if you have `v3` of a game and have an update to `v4` - you may find there are delta files which would:
* Update `v0` -> `v4`
* Update `v1` -> `v4`
* Update `v2` -> `v4`
* Update `v3` -> `v4`

The NSP will also contain a full update NCA which can *replace* the old NCA with the updated one.
Both of these actions (patch or replace) produce the same result.

It would seem some `Scene` teams are able to grab delta files through some *secret sauce* but a normal dump would not grab these but they would still be entries in the `.cnmt.nca` index.

An example of one such NSP is :
```
 PFS0:
 ├── cedfad303ae8d4824e9ef95b1bd7b6c3.nca (238 MB)
 ├── 698022c590f9901daa283a6857936fd9.nca (185 KB)
 ├── 45e27241f3b2c077daa43178f7c68e29.nca (163 KB)
 ├── bfb2aa0d0fd8c9c090756d479aa3ce25.cnmt.nca (5 KB)
 ├── 0100db00117ba800000000000000000b.tik (704 B)
 └── 0100db00117ba800000000000000000b.cert (1 KB)

 Metadata Content:
 ├── [V] cedfad303ae8d4824e9ef95b1bd7b6c3.nca [Program]
 ├── [V] 698022c590f9901daa283a6857936fd9.nca [Control]
 ├── [V] 45e27241f3b2c077daa43178f7c68e29.nca [LegalInformation]
 ├── [X] 12b418892176e337cf4eea652e552986.nca [DeltaFragment] <- Missing
 ├── [X] 74fa18fefb8c61671131e9e4fe43f11e.nca [DeltaFragment] <- Missing
 ├── [X] dc50ac094db2439d47f6db42c3942734.nca [DeltaFragment] <- Missing
 └── [X] ead1bf7228623559eb7b88c52d17a2a6.nca [DeltaFragment] <- Missing
```
.. this NSP has 4 delta files listed in the `.cnmt.nca` but are missing from the NSP file-system.

# Proposal

With this understanding of the NSP file format and the development of various NSP tools, it is entirely possible to sort and validate the files within an NSP and arrange them in such a way as to, not only, be consistent but also not effect any essential data in such a way that would invalidate preservation.

Due to this, it is proposed that we embrace a "Standard NSP" format:

1. All NCAs must pass validation (Header signature + SHA256 hash).
1. No `xml`,`jpg` or other loose files.
1. NCA files are added in the order they are defined in `.cnmt.nca` file.
1. NCAs must match the size and type specified in the `.cnmt.nca` file.
1. Delta-fragment files will be skipped (see below)
1. `.cnmt.nca` is added after other NCAs.
1. Add normalised `.tik` (if required)
1. Add `common.cert` as `.cert` (if required) - This is a 0x700-byte long common certificate chain file (with SHA256 of `3c4f20dca231655e90c75b3e9689e4dd38135401029ab1f2ea32d1c2573f1dfe`).

When NCAs use titlekey encryption, there are a set of conditions the ticket must ahere to. If any are incorrect, then the ticket must be re-written.

The conditions are as follows:
1. RSA-2048-PKCS#1 v1.5 Signature (`0x100` bytes @ `0x004`): must be wiped by setting all of its bytes to `0xFF`.
1. Signature Issuer (`0x40` bytes @ `0x140`): must be set to `Root-CA00000003-XS00000020`. The rest of the bytes must be set to zero.
1. Titlekey Block (`0x100` bytes @ `0x180`): must be wiped by setting all of its bytes to zero. Its first 16 bytes must then be replaced with the titlekek-encrypted titlekey for this title -- which is always the result of decrypting the original 0x100-byte long block using RSA-2048-OAEP + console-specific keydata.
1. Titlekey Type (`0x1` byte @ `0x281`): must be set to 0 ("Common").
1. License Type (`0x1` byte @ `0x284`): must be set to 0 ("Permanent").
1. Property Mask (`0x2` bytes @ `0x286`): must be set to 0 ("None").
1. Ticket ID (`0x8` bytes @ `0x290`): must be zeroed out.
1. Device ID (`0x8` bytes @ `0x298`): must be zeroed out.
1. Account ID (`0x4` bytes @ `0x2B0`): must be zeroed out.
1. Section Records Total Size (`0x4` bytes @ `0x2B4`): must be zeroed out.
1. Section Records Header Offset (`0x4` bytes @ `0x2B8`): must be set to `0x2C0` (little endian).
1. Section Records Header Count (`0x2` bytes @ `0x2BC`): must be zeroed out.
1. Section Records Entry Size (`0x2` bytes @ `0x2BE`): must be zeroed out.

### Wat ? Re-writing the Ticket ? What about preservation ?
The ticket(`.tik`) file exists to hold the decrypted titlekey so that the NSP can decrypt the NCAs. There are a series of extra properties and flags that hold personal identifiable information (PII) data about the original dumping console such as device id and account id. This obviously needs to be removed but different dumping tools might have missed some of these so its best we check and remove where found. None of this data effects preservation.

### Should Delta-fragment files be included or excluded ?

This can be argued both ways:
1. Delta files are just patches - The full update NCA is present in the NSP so delta files are irrelevant. The result is the same.
2. Delta files should be preserved as well - Preserve all the things & dont throw anything away.

The problem with preserving deltas is that deltas may or may not be present. Currently, the only way to *get* delta files is through a technique most people dont have access to.

This is an important point for preservation - if an average person dumps an NSP, they will not get delta files. If a "Scene" team dumps an NSP, they *might* get delta files. So we are again in a situation where the same NSP dumped by different people will have different results.
The goal of this format is to allow validation from multiple sources and get the same result.

For this reason, for now, it is proposed that delta files are excluded from the "Standard NSP" format.

### Conversion to Standard NSP
So for our example a "Standard NSP" version would now look like:
```
PFS0:
 ├── 671d172e7993ee033d1be25ee76378e3.nca
 ├── a0f6f6c8ac3b97885d43cabc4473925f.nca
 ├── ac89f1af8740787d87d64fe9e5dc66e7.nca      <- Now in correct order
 ├── 01040719881839ccd0a49b36bbc11934.nca
 ├── 317e048a8e3a4c92f072b33378106f37.cnmt.nca <- Now in correct order
 ├── 010025400aece0000000000000000005.tik      <- Normalised
 └── 010025400aece0000000000000000005.cert
```

Its `normalised` ticket file would look like:

```
 ┌─────────────────┬──────────────────────────────────────┐
 │ Issuer          │ Root-CA00000003-XS00000020           │
 │ Format Version  │ 0x2                                  │
 │ TitleKey Type   │ Common                               │
 │ Ticket Id       │ Not Set                              │
 │ Ticket Version  │ Not Set                              │
 │ License Type    │ Permanent                            │
 │ Crypto Revision │ 0x5                                  │
 │ Device Id       │ Not Set                              │
 │ Account Id      │ Not Set                              │
 │ Rights Id       │ 010025400AECE0000000000000000005     │
 │ Signature Type  │ Rsa2048Sha256                        │
 │ Properties      │ ┌──────────────────────────┬───────┐ │
 │                 │ │ Pre-Install ?            │ False │ │
 │                 │ │ Allow All Content ?      │ False │ │
 │                 │ │ Shared Title ?           │ False │ │
 │                 │ │ DeviceLink Independent ? │ False │ │
 │                 │ │ Volatile ?               │ False │ │
 │                 │ │ E-License Required ?     │ False │ │
 │                 │ └──────────────────────────┴───────┘ │
 └─────────────────┴──────────────────────────────────────┘
```

## Variations

(Need some help here .. what are the possible variations that are currently known about?)

* Same title Id + version - but some other slight difference (metadata - see example below)
* Same game or update + version - but different title-id (regional variation? - example?)
* DLC Unlockers - These are often hacked NCAs or some other "Homebrew" version of an NSP. While they exist and should be DAT'd *somewhere* they probably fall out of the scope of "Standard NSP" as they will often fail all validation.
---

## Extended examples from "the wild"
### Variations in metadata

An example where 2 NSPs - identical files apart from a difference in `Minimum System Version`

All validation passes (which means the files are signed correctly and are "Big N" issued)
```
 ┌───────────────────────────────────────┬───────────────────────────────────────────┐
 │ Filename                              │ 141daede57a1abd9e6b0b92431a7aadf.cnmt.nca │
 │ SHA256 Hash                           │ 141daede57a1abd9e6b0b92431a7aadf          │
 │ Header Validity                       │ Valid                                     │
 │ Type                                  │ Patch                                     │
 │ Title Version                         │ 0.1.0.0                                   │
 │ Title Id                              │ 0100c1f0051b6800                          │
 │ Minimum App Version                   │ NOT SET                                   │
 │ Minimum System Version                │ 4.1.0.50 (0x10100032) <-HERE              │
 └───────────────────────────────────────┴───────────────────────────────────────────┘
```

```
 ┌───────────────────────────────────────┬───────────────────────────────────────────┐
 │ Filename                              │ 1dbc2ffd7ff2e9a48abe7a836dda7401.cnmt.nca │
 │ SHA256 Hash                           │ 1dbc2ffd7ff2e9a48abe7a836dda7401          │
 │ Header Validity                       │ Valid                                     │
 │ Type                                  │ Patch                                     │
 │ Title Version                         │ 0.1.0.0                                   │
 │ Title Id                              │ 0100c1f0051b6800                          │
 │ Minimum App Version                   │ NOT SET                                   │
 │ Minimum System Version                │ 4.1.0.0 (0x10100000) <-HERE               │
 └───────────────────────────────────────┴───────────────────────────────────────────┘
```

The "Title Version" field has the granularity for revisions.

If the software had changed and it was a new Title Version revision (e.g Title Version `0.1.0.0` -> `0.1.0.1`), then the release should be tagged as another revision. (e.g `(Rev1)`).

In this case, the software is identical but only metadata is changed so it would be considered an alternate version (`(Alt1)`).

## Why not just use the CDN set ?
Good question, extracting an NSP into CDN format (effectively splitting out all the NCAs + title-key info into individual files) is great and already validates and catches a lot of the issues named above.
The main reason for keeping it as an NSP is just that it is a more portable format, one single file, rather than a collection of loose files.The NSP itself can be validated as a whole, faster than loading all those individual files.
