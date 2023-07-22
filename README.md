# Infolink

# Summary

Infolink's is all-in-one solution to solving integration with third
parties, automating workflows with exchanges coming from all forms of
requests, ranging from internal messages to files dumped on a server.

It is divided into:

- Documents (A definition of an Xchange)

- Xchange (A instance of a document, containing the data)

- Partners (Third parties and their API Credentials)

- Subscribers (The listeners and orchestrator's of Xchanges)

And they all come together to formulate a solution to messaging and
background-job handling, while being ready for the cloud.

# The Stack

Infolink depends on a few things to operate, and they are all
cloud-friendly.

- A message-broker (like _RabbitMQ_, but not necessarily)

- S3 Standard Bucket

- Serverless adapters

All these things are built to run on the cloud and Infolink communicates
with them seamlessly. Furthermore, due to the way Infolink is packaged
and dockerized, pushing it to a kubernetes cluster and running it
natively on the cloud is extremely plug-and-play.

# Introduction

Infolink is a solution to many modern problems in programming, and
especially in the logistics field where there is constant communication
between different parties. It abstracts complex architectural issues
like messaging, watching files and ad-hoc endpoints.

It is built and depends on messaging-brokers like _RabbitMQ_, which are
known for being efficient at handling a massive volume of messages.
Infolink makes full use of that, and removes all the tedious need for
setup that comes with these messaging-brokers by automatically handling
Queues and exchanges.

Infolink's interface is divided up into digestible segments, all
accessible through its straightforward API. Therefore, it can be easily
integrated into any form of user interface, or service. The segments are
divided and explained thoroughly below.

# Documents

Documents in Infolink are a way to define basic information about an
_Xchange_. Things like its Id, name, whether it's incoming from the
messaging bus (and what is the message called), along with **Promoted
Properties**

## Promoted Properties

Promoted properties are Infolink's way to facilitate filtering Xchanges
(Instances of a document). For example, if the Xchange's body looks like
this:

    {
        "color": "red",
        "details": {
            "modelNumber": "11G4",
            "manufacturer": "ACompany"
        }
    }

A sample promoted property could be: **details.manufacturer**, and then
the Xchange's destination could be filtered based on what the
manufacturer is equal to.

# Xchanges

Xchanges in Infolink can be anything, really. It could be an API request
a third party sent, a message sent through the messaging bus, or it
could be a file that was sent over FTP. This abstraction layer makes
dealing with all these types of messages in a unified way very easy.
**No longer will the developer have to dedicate resources to handling
different methods of requests**, they can deal with the data within the
Xchange as is.

Furthermore, the xchange are _immutable_, which becomes invaluable when
it comes to performance (_No update statements in operations_), and
makes tracking statistics of use incredibly straightforward.

They are represented by their input, output and their status (Running,
Fail, Success). Due to the status monitoring, failed messages will be
logged and can be retried (Whether automatically or manually). Also, if
an xchange does happen to fail, the exact reason will be reported
through exceptions being logged in the failure messages. This makes
debugging the cause of failure less of a guessing game and a lot more
productive.

## Xchange's Journey

The Xchange begins with _input_ coming in from any form of request like
the ones mentioned above. This input is kept and then sent to the Mapper
(if applicable), which gives the _output_. Then, this output is sent to
the Handler, which will use this output to do some task and then finally
return the _Response_.

## Data Storage

The Xchange's input and output and response are kept on an S3 standard
bucket. This facilitates easy archiving of the happenings of an Xchange,
along with accessibility to see the results of an Xchange without going
to some third party logging website.

# Partners

The partners in Infolink is one of the more straightforward
abstractions. It entails defining a third party that a particular
Subscriber is related to. Furthermore, API credentials can be attached
to this partner to facilitate automating authentication when
communicating with their services.

# Subscriptions

Subscriptions are the bread and butter of Infolink. They sort of 'create
the journey' for the Xchange to go through. They listen for Xchanges and
then run the defined tasks within in a mini-workflow.

## Types of Subscribers

There are four types of subscriptions currently available in Infolink:

- Internal

- API Call

- Receiving

- Aggregation

### Internal

Internal Subscriptions are built for listening for messages being run
through the internal messaging bus. These rely on a publisher/consumer
architecture and allow for easy background-job handling operations, like
informing a third-party agent of a new trace on a shipment when it is
scanned on one's system.

The messages are sent with their class type name as the message's name,
and this information needs to be defined in the document as mentioned
above. After that is set up, when a message with that type name is
published to the messaging queue, the subscriber will take its data as
input and send it through its workflow.

### API Call

The API call subscribers operate as sort of ad-hoc endpoints. This
allows to cater to third parties that require an endpoint embedded in
one's system while still **maintaining the integrity of the codebase**.

The API call's body would then be transposed to an Xchange in the system
and again passed to the workflow.

### Receiving

The Receiving subscriber is immensely helpful at handling input that
usually comes in the form of files and the like. Very often, third
parties will send data in the form of files over SFTP, or on the cloud
somewhere. Dealing with these files in an organized manner, where they
are processed in a fail-safe, scheduled manner is the Receiver
subscriber's job.

Furthermore, processed files can be automatically acknowledged so no
duplicate processing occurs (depending on how the Receiver is defined.)

These subscribers can be configured to have multiple types of schedules,
such as hourly, daily or a specific day of the month. Multiple schedules
can be defined which make it very flexible.

### Aggregation

The Aggregation subscriber would combine multiple Xchanges into one,
allowing to send data in bulk to third parties that do not accept
one-by-one requests.

## Scheduler service

You can set the recurrence for the receiver to run on hourly, daily, weekly or monthly basis.

### Hourly

Setting the minutes value will trigger the receiver to run once you reach the number of minutes specified every hour.

For example, if you set 2 schedules as below:

1. hourly (min 0)
2. hourly (min 30)

The receiver will run twice an hour at min 0 and 30 (every half an hour).

### Daily

Setting the hour and minute values will trigger the receiver to run once you reach the time of the day specified.

For example, if you set 2 schedules as below:

1. Daily (h 4 min 0)
2. Daily (h 16 min 0)

The receiver will run twice a day at 4:00 AM and 4:00 PM

### Weekly

Setting the day, hour and minute values will trigger the receiver to run once you reach the day and time specified every
week.

For example, if you set 2 schedules as below:

1. Weekly (d 1 h 4 min 0)
2. Weekly (d 7 h 16 min 0)

The receiver will run twice a week on the first day of the week at 4:00 AM and on the 7th day at 4:00 PM.

### Monthly

Setting the day, hour and minute values will trigger the receiver to run once you reach the day and time specified every
month.

For example, if you set 2 schedules as below:

1. Monthly (d 1 h 4 min 0)
2. Monthly (d 28 h 16 min 0)

The receiver will run twice a month on the first day of the month at 4:00 AM and on the 28th day at 4:00 PM.

### Setting Backward

When setting the backward schedule, the receiver will run once the day and time reaches the value backwardly from the
end of the recurrence.

For example, if you set the below schedule:

1. Monthly (d 1, h 23, min 59) Backward

The receiver will run monthly on the last day of the month at 11:59 pm

## Workflow

### Filtration

As mentioned in the Document section, promoted properties can be defined
which allow for filtration. This is where the aforementioned filtration
occurs. Constraints like:

- Promoted property x.y should equal z

- Promoted property x should be one of y,z,r

- Etc..

Make it incredibly straightforward to only run the subscriber for the
relevant Xchange and only the relevant Xchange.

### Mapping

The Mapper in Infolink is a serverless adapter(Covered below) that
should be in charge of converting the Xchange's data from one shape to
another. That could be from JSON to a specific XML Schema, or from one
JSON schema to another and so on.

The mapper will do its job converting the document's shape, and then its
result will be forwarded to the Handler.

One such mapper is the **Liquid** mapper, which allow on-the-fly mapping
configuration by passing a configuration to the mapper.

### Handling

Like the mapper above, the handler is also a serverless adapter. Usually
these adapters do a certain task, like send an HTTP request, upload a
file over FTP and so on. The possibilities are really endless, as
explained below, a serverless adapter is just a console app.

We provide a plethora of handers, like:

- HTTP handler to send HTTP requests using the Xchange's data.

- FTP Handler to send files over FTP

- MailGun Handler and SendGrid to send emails.

- Many more!

However, if the existing handlers do not fulfill a task, one can be
programmed easily. Creating an adapter is covered below.

## Response Subscription

Sometimes, a simple mapper to handler scenario does not encapsulate a
business need and a longer workflow is required.

This is where Response Subscribers come in. It is possible to chain
subscribers together by configuring a Response subscriber. What this
leads to is that once one subscriber's workflow is done, it will simply
forward the result to the input of another subscriber.

This implies that 'infinite' chaining can occur to fulfill any business
need.

# Serverless Adapters

We provide an implementation of serverless that depends on S3 buckets.
Basically, any console application can be written and have the interface
`IInfolinkHandler` (_for C\#_) applied to it. Once the interface is
fulfilled and the logic is programmed, the adapter can be installed
using the Electron Serverless Installer provided which packages and
pushes the console application to the cloud. Where, if it follows the
naming convention, it can be easily configured as a handler.

Moreover, due to the nature of them being console applications, unit
testing them and ensuring they perform as intended would be very
straightforward. To make this process easier, our Serverless SDK
provides a _MockRun_ to simulate how the serverless adapter would be
run.

## How to install serverless adapters

#### Requirements

- S3 Compliant Storage or Azure Storage
- [Serverless Installer cli](https://github.com/simplify9/Serverless)



#### Steps 
1. Create net6 console application 
2. Use one of the [default adapters](https://github.com/simplify9/InfolinkAdapters/tree/net6) or Create a handler class that implements **IInfolinkHandler**, **IInfolinkValidator** or **IInfolinkReceiver**
```csharp
using SW.PrimitiveTypes;
using SW.Serverless.Sdk;

 public class Handler: IInfolinkHandler
    {
        public async Task<XchangeFile> Handle(XchangeFile xchangeFile)
        {
            var data = xchangeFile.Data;
            var model = JsonConvert.DeserializeObject<Data>(data);
           // some operations on the data
            return  await  Task.FromResult(new XchangeFile(JsonConvert.SerializeObject(model)));

        }
    }
```
3. Setup The main function
```csharp
 using SW.Serverless.Sdk;
 
 public static class Program
    {
        private static async Task Main() => await Runner.Run(new Handler());
    }
```
4. Build and upload the adapter to the storage service
   1. clone the [Serverless repository] 
   2. run the Serverless installer with the following arguments
   ```
   <Path*>
   -a
   <Access id Key>
   -s
   <Secret Access Key>
   -b
   <Bucket name> 
   -u
   <Storage url>
   ```
   **The path name must start with infolink6.< mappers | handlers | receivers >.< any name >** 
   ex: infolink6.mappers.simplyProject.outMapper 
   **If you are using azure storage please add "-p as" to the cli arguments 

5. Now the adapter will be available for selection from the subscription editor

# Conclusion

Infolink is a flexible solution to various problems that come up with
integration, background-jobs and the like. Its serverless-based
architecture allows for completely unique logic to run in the
easily-configured subscription workflows.

The way workflows can be seamlessly configured and how Xchanges can be
any form of request facilitates produciticity and focusing on the logic
of the transaction, rather than how to go about sending, receiving and
logging the transaction.
