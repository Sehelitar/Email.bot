# Email.bot - Email notifications for Streamer.bot

This extension for Streamer.bot triggers an event for incoming emails on your Gmail account.

** **WARNING** **
Compatible only with Streamer.bot **> 0.2.0**


## Compilation

To build this extension, you only need a few things :

* nuget dependencies freshly installed
* DLL files from a Streamer.bot install (a fresh install will do, no need for a fully configured one).
* A ``credentials.json`` file to access Google's Gmail API (see https://support.google.com/googleapi/answer/6158862)

## Installation

To install this extension, go to the ``bin\Release\net4.7.2`` directory and copy both ``Email.bot.dll`` and 
``Google.Apis.Gmail.v1.dll`` files into ``dlls`` folder of your Streamer.bot installation.
Then, import ``actions.txt`` into Streamer.bot using the Import button in the top toolbar of the app.

If the installation is successful, a configuration window will open. If not, check if the code in the imported action
can compile successfuly and have no missing references. 

Configure everything to your liking and then "Save". A Windows notification will appear if everything is fine, you will get a
error message otherwise.

## Usage

Once the extension is loaded and working, a new trigger should be available for use. You will find it under
``Custom > Email > New email received``.
This trigger run for every email received in your mailbox matching the label and filters configured.

Some arguments are provided :

| Header | Type   | Value  |
| :----- | :----- | :----- |
| emailId | string| Email unique ID |
| emailDate | DateTime | DateTime of the moment this email was sent |
| emailFrom | string | Sender infos - RFC 822 (ex: ``"Bilbo Baggins" <bilbo@mordor.com>``)
| emailSubject | string | Email subject |

2 C# methods are available to fetch emails bodies :
* FetchEmailHtmlBody
* FetchEmailTextBody

Both methods require to have ``emailId`` argument set with the email id.
They will both set the argument ``emailBody`` (string) only if a matching content body can be found, it will stay undefined is there's no matches.

## Licence

This project is distributed under MIT License (see LICENSE)