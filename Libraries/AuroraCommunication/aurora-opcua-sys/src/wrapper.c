#include "open62541.h"
#include <stdlib.h>
#include <string.h>

typedef struct AuroraOpcClient { UA_Client *client; } AuroraOpcClient;
typedef struct AuroraOpcScalar { uint32_t type; uint32_t length; unsigned char data[16]; } AuroraOpcScalar;

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
                                            const unsigned char *trust, size_t trust_len,
                                            const char *username, const char *password) {
    AuroraOpcClient *value = (AuroraOpcClient*)calloc(1, sizeof(AuroraOpcClient));
    if(!value) return NULL;
    value->client = UA_Client_new();
    if(!value->client) { free(value); return NULL; }
    UA_ClientConfig *config = UA_Client_getConfig(value->client);
    UA_StatusCode code;
    if(certificate_len && private_key_len && trust_len) {
        UA_ByteString cert = {certificate_len, (UA_Byte*)certificate};
        UA_ByteString key = {private_key_len, (UA_Byte*)private_key};
        UA_ByteString trusted = {trust_len, (UA_Byte*)trust};
        code = UA_ClientConfig_setDefaultEncryption(config, cert, key, &trusted, 1, NULL, 0);
    } else {
        code = UA_ClientConfig_setDefault(config);
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
    if(code == UA_STATUSCODE_GOOD && UA_Variant_isScalar(&value)) {
        const UA_DataType *types[] = { &UA_TYPES[UA_TYPES_BOOLEAN], &UA_TYPES[UA_TYPES_UINT16], &UA_TYPES[UA_TYPES_INT16], &UA_TYPES[UA_TYPES_UINT32], &UA_TYPES[UA_TYPES_INT32], &UA_TYPES[UA_TYPES_UINT64], &UA_TYPES[UA_TYPES_INT64], &UA_TYPES[UA_TYPES_FLOAT], &UA_TYPES[UA_TYPES_DOUBLE] };
        size_t sizes[] = {1,2,2,4,4,8,8,4,8}; code = UA_STATUSCODE_BADTYPEMISMATCH;
        for(uint32_t i=0;i<9;i++) if(UA_Variant_hasScalarType(&value, types[i])) { out->type=i; out->length=(uint32_t)sizes[i]; memcpy(out->data,value.data,sizes[i]); code=UA_STATUSCODE_GOOD; break; }
    }
    UA_Variant_clear(&value); UA_NodeId_clear(&node); return code;
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
