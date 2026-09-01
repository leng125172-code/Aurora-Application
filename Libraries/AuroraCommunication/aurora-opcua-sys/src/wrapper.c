#include "open62541.h"
#include <stdlib.h>
#include <string.h>

typedef struct AuroraOpcScalar { uint32_t type; uint32_t length; unsigned char data[16]; } AuroraOpcScalar;
#define AURORA_OPC_MAX_MONITORS 128
typedef struct AuroraOpcMonitor {
    char node_id[512];
    AuroraOpcScalar value;
    uint64_t sequence;
    uint64_t delivered;
    UA_Boolean used;
} AuroraOpcMonitor;
typedef struct AuroraOpcClient {
    UA_Client *client;
    UA_UInt32 subscription_id;
    AuroraOpcMonitor monitors[AURORA_OPC_MAX_MONITORS];
} AuroraOpcClient;
typedef struct AuroraOpcBrowseNode {
    char node_id[512];
    char browse_name[256];
    char display_name[256];
    uint8_t variable;
} AuroraOpcBrowseNode;
typedef struct AuroraOpcEvent {
    char node_id[512];
    AuroraOpcScalar value;
    uint64_t sequence;
} AuroraOpcEvent;

static UA_StatusCode aurora_scalar_from_variant(const UA_Variant *value, AuroraOpcScalar *out) {
    if(!value || !out || !UA_Variant_isScalar(value)) return UA_STATUSCODE_BADTYPEMISMATCH;
    const UA_DataType *types[] = { &UA_TYPES[UA_TYPES_BOOLEAN], &UA_TYPES[UA_TYPES_UINT16], &UA_TYPES[UA_TYPES_INT16], &UA_TYPES[UA_TYPES_UINT32], &UA_TYPES[UA_TYPES_INT32], &UA_TYPES[UA_TYPES_UINT64], &UA_TYPES[UA_TYPES_INT64], &UA_TYPES[UA_TYPES_FLOAT], &UA_TYPES[UA_TYPES_DOUBLE] };
    size_t sizes[] = {1,2,2,4,4,8,8,4,8};
    for(uint32_t i=0;i<9;i++) {
        if(UA_Variant_hasScalarType(value, types[i])) {
            out->type=i; out->length=(uint32_t)sizes[i]; memcpy(out->data,value->data,sizes[i]);
            return UA_STATUSCODE_GOOD;
        }
    }
    return UA_STATUSCODE_BADTYPEMISMATCH;
}

static void aurora_data_change(UA_Client *client, UA_UInt32 subId, void *subContext,
                               UA_UInt32 monId, void *monContext, UA_DataValue *value) {
    (void)client; (void)subId; (void)subContext; (void)monId;
    AuroraOpcMonitor *monitor = (AuroraOpcMonitor*)monContext;
    if(monitor && value && value->hasValue &&
       aurora_scalar_from_variant(&value->value, &monitor->value) == UA_STATUSCODE_GOOD) {
        monitor->sequence++;
    }
}

static void aurora_copy_ua_string(char *target, size_t capacity, const UA_String *source) {
    if(!target || capacity == 0) return;
    size_t length = source && source->data ? source->length : 0;
    if(length >= capacity) length = capacity - 1;
    if(length) memcpy(target, source->data, length);
    target[length] = '\0';
}

AuroraOpcClient *aurora_opc_new(const char *endpoint) {
    AuroraOpcClient *value = (AuroraOpcClient*)calloc(1, sizeof(AuroraOpcClient));
    if(!value) return NULL;
    value->client = UA_Client_new();
    if(!value->client) { free(value); return NULL; }
    UA_StatusCode code = UA_ClientConfig_setDefault(UA_Client_getConfig(value->client));
    if(code == UA_STATUSCODE_GOOD) code = UA_Client_connect(value->client, endpoint);
    if(code != UA_STATUSCODE_GOOD) { UA_Client_delete(value->client); free(value); return NULL; }
    return value;
}

AuroraOpcClient *aurora_opc_new_configured(const char *endpoint,
                                            const unsigned char *certificate, size_t certificate_len,
                                            const unsigned char *private_key, size_t private_key_len,
                                            const unsigned char *const *trust, const size_t *trust_lengths,
                                            size_t trust_count, uint32_t security_mode,
                                            const char *security_policy,
                                            const char *username, const char *password) {
    AuroraOpcClient *value = (AuroraOpcClient*)calloc(1, sizeof(AuroraOpcClient));
    if(!value) return NULL;
    value->client = UA_Client_new();
    if(!value->client) { free(value); return NULL; }
    UA_ClientConfig *config = UA_Client_getConfig(value->client);
    UA_StatusCode code;
    if(certificate_len && private_key_len && trust_count) {
        UA_ByteString cert = {certificate_len, (UA_Byte*)certificate};
        UA_ByteString key = {private_key_len, (UA_Byte*)private_key};
        UA_ByteString *trusted = (UA_ByteString*)calloc(trust_count, sizeof(UA_ByteString));
        if(!trusted) { UA_Client_delete(value->client); free(value); return NULL; }
        for(size_t index=0; index<trust_count; index++) {
            trusted[index].length = trust_lengths[index];
            trusted[index].data = (UA_Byte*)trust[index];
        }
        code = UA_ClientConfig_setDefaultEncryption(config, cert, key, trusted, trust_count, NULL, 0);
        free(trusted);
    } else {
        code = UA_ClientConfig_setDefault(config);
    }
    if(code == UA_STATUSCODE_GOOD) {
        config->securityMode = (UA_MessageSecurityMode)security_mode;
        UA_String_clear(&config->securityPolicyUri);
        config->securityPolicyUri = UA_STRING_ALLOC(security_policy);
        if(!config->securityPolicyUri.data) code = UA_STATUSCODE_BADOUTOFMEMORY;
    }
    if(code == UA_STATUSCODE_GOOD) {
        code = username ? UA_Client_connectUsername(value->client, endpoint, username, password ? password : "")
                        : UA_Client_connect(value->client, endpoint);
    }
    if(code != UA_STATUSCODE_GOOD) { UA_Client_delete(value->client); free(value); return NULL; }
    return value;
}

void aurora_opc_delete(AuroraOpcClient *value) {
    if(!value) return;
    if(value->client) { UA_Client_disconnect(value->client); UA_Client_delete(value->client); }
    free(value);
}

UA_StatusCode aurora_opc_read_scalar(AuroraOpcClient *client, const char *text, AuroraOpcScalar *out) {
    if(!client || !text || !out) return UA_STATUSCODE_BADINVALIDARGUMENT;
    UA_NodeId node = UA_NODEID_NULL; UA_Variant value; UA_Variant_init(&value);
    UA_StatusCode code = UA_NodeId_parse(&node, UA_STRING((char*)text));
    if(code == UA_STATUSCODE_GOOD) code = UA_Client_readValueAttribute(client->client, node, &value);
    if(code == UA_STATUSCODE_GOOD) code = aurora_scalar_from_variant(&value, out);
    UA_Variant_clear(&value); UA_NodeId_clear(&node); return code;
}

UA_StatusCode aurora_opc_browse(AuroraOpcClient *client, const char *text,
                                AuroraOpcBrowseNode *nodes, size_t capacity, size_t *written) {
    if(!client || !text || !nodes || !written) return UA_STATUSCODE_BADINVALIDARGUMENT;
    *written = 0;
    UA_BrowseDescription description; UA_BrowseDescription_init(&description);
    UA_StatusCode code = UA_NodeId_parse(&description.nodeId, UA_STRING((char*)text));
    if(code != UA_STATUSCODE_GOOD) return code;
    description.browseDirection = UA_BROWSEDIRECTION_FORWARD;
    description.referenceTypeId = UA_NODEID_NUMERIC(0, UA_NS0ID_HIERARCHICALREFERENCES);
    description.includeSubtypes = true;
    description.nodeClassMask = 0;
    description.resultMask = UA_BROWSERESULTMASK_ALL;
    UA_BrowseResult result = UA_Client_browse(client->client, NULL, 0, &description);
    while(result.statusCode == UA_STATUSCODE_GOOD) {
        for(size_t index=0; index<result.referencesSize && *written<capacity; index++) {
            UA_ReferenceDescription *reference = &result.references[index];
            AuroraOpcBrowseNode *output = &nodes[(*written)++];
            memset(output, 0, sizeof(*output));
            UA_String printed = UA_STRING_NULL;
            if(UA_NodeId_print(&reference->nodeId.nodeId, &printed) == UA_STATUSCODE_GOOD) {
                aurora_copy_ua_string(output->node_id, sizeof(output->node_id), &printed);
            }
            UA_String_clear(&printed);
            aurora_copy_ua_string(output->browse_name, sizeof(output->browse_name), &reference->browseName.name);
            aurora_copy_ua_string(output->display_name, sizeof(output->display_name), &reference->displayName.text);
            output->variable = reference->nodeClass == UA_NODECLASS_VARIABLE;
        }
        if(result.continuationPoint.length == 0) break;
        if(*written >= capacity) {
            UA_BrowseResult released = UA_Client_browseNext(client->client, true,
                                                            result.continuationPoint);
            UA_BrowseResult_clear(&released);
            break;
        }
        UA_ByteString continuation = UA_BYTESTRING_NULL;
        code = UA_ByteString_copy(&result.continuationPoint, &continuation);
        UA_BrowseResult_clear(&result);
        if(code != UA_STATUSCODE_GOOD) break;
        result = UA_Client_browseNext(client->client, false, continuation);
        UA_ByteString_clear(&continuation);
    }
    if(code == UA_STATUSCODE_GOOD) code = result.statusCode;
    UA_BrowseResult_clear(&result); UA_BrowseDescription_clear(&description);
    return code;
}

UA_StatusCode aurora_opc_subscribe(AuroraOpcClient *client, const char *text,
                                   double sampling_interval) {
    if(!client || !text) return UA_STATUSCODE_BADINVALIDARGUMENT;
    if(client->subscription_id == 0) {
        UA_CreateSubscriptionRequest request = UA_CreateSubscriptionRequest_default();
        UA_CreateSubscriptionResponse response = UA_Client_Subscriptions_create(client->client, request, NULL, NULL, NULL);
        UA_StatusCode code = response.responseHeader.serviceResult;
        if(code == UA_STATUSCODE_GOOD) client->subscription_id = response.subscriptionId;
        UA_CreateSubscriptionResponse_clear(&response);
        if(code != UA_STATUSCODE_GOOD) return code;
    }
    AuroraOpcMonitor *monitor = NULL;
    for(size_t index=0; index<AURORA_OPC_MAX_MONITORS; index++) {
        if(!client->monitors[index].used) { monitor=&client->monitors[index]; break; }
    }
    if(!monitor) return UA_STATUSCODE_BADTOOMANYMONITOREDITEMS;
    memset(monitor, 0, sizeof(*monitor)); monitor->used = true;
    strncpy(monitor->node_id, text, sizeof(monitor->node_id)-1);
    UA_NodeId node = UA_NODEID_NULL;
    UA_StatusCode code = UA_NodeId_parse(&node, UA_STRING((char*)text));
    if(code == UA_STATUSCODE_GOOD) {
        UA_MonitoredItemCreateRequest request = UA_MonitoredItemCreateRequest_default(node);
        request.requestedParameters.samplingInterval = sampling_interval;
        UA_MonitoredItemCreateResult result = UA_Client_MonitoredItems_createDataChange(
            client->client, client->subscription_id, UA_TIMESTAMPSTORETURN_BOTH,
            request, monitor, aurora_data_change, NULL);
        code = result.statusCode;
        UA_MonitoredItemCreateResult_clear(&result);
    }
    UA_NodeId_clear(&node);
    if(code != UA_STATUSCODE_GOOD) memset(monitor, 0, sizeof(*monitor));
    return code;
}

UA_StatusCode aurora_opc_iterate(AuroraOpcClient *client, uint32_t timeout,
                                 AuroraOpcEvent *event, uint8_t *has_event) {
    if(!client || !event || !has_event) return UA_STATUSCODE_BADINVALIDARGUMENT;
    *has_event = 0;
    UA_StatusCode code = UA_Client_run_iterate(client->client, timeout);
    if(code != UA_STATUSCODE_GOOD) return code;
    for(size_t index=0; index<AURORA_OPC_MAX_MONITORS; index++) {
        AuroraOpcMonitor *monitor = &client->monitors[index];
        if(monitor->used && monitor->sequence != monitor->delivered) {
            memset(event, 0, sizeof(*event));
            strncpy(event->node_id, monitor->node_id, sizeof(event->node_id)-1);
            event->value = monitor->value; event->sequence = monitor->sequence;
            monitor->delivered = monitor->sequence; *has_event = 1; break;
        }
    }
    return UA_STATUSCODE_GOOD;
}

UA_StatusCode aurora_opc_write_scalar(AuroraOpcClient *client, const char *text, const AuroraOpcScalar *input) {
    if(!client || !text || !input || input->type >= 9) return UA_STATUSCODE_BADINVALIDARGUMENT;
    const UA_DataType *types[] = { &UA_TYPES[UA_TYPES_BOOLEAN], &UA_TYPES[UA_TYPES_UINT16], &UA_TYPES[UA_TYPES_INT16], &UA_TYPES[UA_TYPES_UINT32], &UA_TYPES[UA_TYPES_INT32], &UA_TYPES[UA_TYPES_UINT64], &UA_TYPES[UA_TYPES_INT64], &UA_TYPES[UA_TYPES_FLOAT], &UA_TYPES[UA_TYPES_DOUBLE] };
    UA_NodeId node=UA_NODEID_NULL; UA_Variant value; UA_Variant_init(&value);
    UA_StatusCode code=UA_NodeId_parse(&node,UA_STRING((char*)text));
    if(code==UA_STATUSCODE_GOOD) code=UA_Variant_setScalarCopy(&value,input->data,types[input->type]);
    if(code==UA_STATUSCODE_GOOD) code=UA_Client_writeValueAttribute(client->client,node,&value);
    UA_Variant_clear(&value); UA_NodeId_clear(&node); return code;
}
